import SwiftUI

/// Name of the coordinate space the chord flow reports chip frames in. File-scope rather than a
/// `static` on `GenerateTrackView` so it can be read from `.onGeometryChange`'s `@Sendable`
/// transform closure — a `static` on a `@MainActor` `View` is main-actor-isolated and can't be.
private let chordFlowCoordinateSpace = "chordFlow"

/// The app's main screen: choose the parameters of a backing track and request generation.
///
/// Laid out as a `ScrollView` of hand-built `SectionCard`s rather than a `Form`. The chord chip
/// flow needs drag-and-drop for its reordering, and both the drop modifiers and a custom drag
/// gesture are unreliable inside a `List`/`Form` on iOS — the list's own scroll gesture
/// recogniser competes for the touch. A plain `ScrollView` doesn't have that problem.
///
/// Precise mode only for now — Vibe mode's backend doesn't exist yet (see `GenerationMode`).
/// `generate()` submits the form to `POST /tracks/generate` via `GenerateTrackModel`; on
/// acceptance `GenerationProgressView` is presented as a `fullScreenCover` to follow the job to
/// completion, and a submission error is shown as an alert on the form.
struct GenerateTrackView: View {
    // `@State` (not `@StateObject`) is how the current Observation model holds a reference type —
    // the view owns this instance for its lifetime and SwiftUI tracks reads into it. Injectable
    // (same idea as `SettingsView`'s store) so previews can seed a populated form.
    @State private var model: GenerateTrackModel

    init(model: GenerateTrackModel = GenerateTrackModel()) {
        // `State(initialValue:)` is the supported way to seed `@State` from an initializer; it's
        // used only on the view's first construction, not on later re-inits.
        _model = State(initialValue: model)
        // Start in custom mode when the model arrives with a signature the quick-pick menu can't
        // represent — otherwise that menu would render with no selection.
        _isCustomTimeSignature = State(initialValue: !model.timeSignature.isCommon)
    }

    @State private var isShowingSettings = false
    @State private var isShowingAddChord = false
    @State private var isShowingVibeComingSoon = false
    // Toggling this reveals the custom beat-count / beat-unit pickers instead of the quick-pick
    // list. Kept in the view (not the model) — it's a display choice, not part of the request.
    @State private var isCustomTimeSignature: Bool
    // Each chord chip's frame in the flow's coordinate space, so a drop can be turned into an
    // insertion position. Rebuilt as the flow lays out; pruned when chords change.
    @State private var chipFrames: [UUID: CGRect] = [:]

    // `body` is deliberately thin — the card stack, toolbar, bottom bar and modals are split into
    // their own properties below. Kept together they form one expression big enough that the
    // type-checker struggles with it.
    var body: some View {
        NavigationStack {
            cardStack
                .background(Color(.systemGroupedBackground))
                .navigationTitle("New Track")
                .toolbar { settingsToolbarItem }
                .safeAreaInset(edge: .bottom) { generateBar }
                .modifier(GenerateTrackModals(
                    model: model,
                    isShowingSettings: $isShowingSettings,
                    isShowingAddChord: $isShowingAddChord,
                    isShowingVibeComingSoon: $isShowingVibeComingSoon
                ))
        }
    }

    private var cardStack: some View {
        ScrollView {
            VStack(spacing: 22) {
                modeCard
                keyCard
                chordsCard
                tempoCard
                timeSignatureCard
                styleCard
            }
            .padding(.vertical, 16)
        }
    }

    private var settingsToolbarItem: some ToolbarContent {
        ToolbarItem(placement: .topBarTrailing) {
            Button {
                isShowingSettings = true
            } label: {
                Label("Settings", systemImage: "gearshape")
            }
        }
    }

    /// Primary action, pinned above the home indicator rather than scrolling away with the form.
    private var generateBar: some View {
        Button {
            // `submitGeneration` is `async`; a `Task` bridges the synchronous button action to
            // it. The model is `@MainActor`, so the work hops back to the main actor to publish
            // its result — no manual dispatch needed.
            Task { await model.generate() }
        } label: {
            // Swap the label for a spinner while the request is in flight; keep it full-width
            // either way so the bar doesn't resize.
            Group {
                if model.isSubmitting {
                    ProgressView()
                } else {
                    Text("Generate Track")
                }
            }
            .frame(maxWidth: .infinity)
        }
        .buttonStyle(.borderedProminent)
        .controlSize(.large)
        .disabled(!model.canGenerate || model.isSubmitting)
        .padding()
        .background(.bar)
    }

    // MARK: - Cards

    private var modeCard: some View {
        SectionCard(footer: Text(model.mode.summary)) {
            // A custom binding rather than `$model.mode`: tapping an unavailable mode never
            // updates the source of truth, so the segmented control stays on Precise instead of
            // visibly jumping to Vibe and back.
            Picker("Mode", selection: Binding(
                get: { model.mode },
                set: { newMode in
                    if newMode.isAvailable {
                        model.mode = newMode
                    } else {
                        isShowingVibeComingSoon = true
                    }
                }
            )) {
                ForEach(GenerationMode.allCases) { mode in
                    Text(mode.displayName).tag(mode)
                }
            }
            .pickerStyle(.segmented)
            .padding(12)
        }
    }

    private var keyCard: some View {
        SectionCard(
            "Key Root",
            footer: Text("The tonal centre. Major or minor isn't sent separately — your chords imply it.")
        ) {
            CardRow("Key Root") {
                Picker("Key Root", selection: $model.keyRoot) {
                    ForEach(NoteName.allCases) { note in
                        Text(note.displayName).tag(note)
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
            }
        }
    }

    /// Each chip is only a drag *source* (`.draggable`); a single `.dropDestination` covering this
    /// whole block turns the drop location into an insertion index using the chip frames tracked
    /// in `chipFrames`. One drop target rather than one per chip is what makes every position —
    /// including the last, and the empty space past it — reachable, and leaves nothing to get
    /// stuck when a drag ends somewhere unexpected.
    private var chordsCard: some View {
        SectionCard("Chords", footer: chordsFooter) {
            VStack(alignment: .leading, spacing: 12) {
                if model.chords.isEmpty {
                    Text("Add at least one chord to build the progression.")
                        .foregroundStyle(.secondary)
                } else {
                    chordChipFlow
                }

                Button {
                    isShowingAddChord = true
                } label: {
                    Label("Add Chord", systemImage: "plus")
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            // The drop zone is the whole card body, not just the chips — so a chord dragged
            // loosely toward the end (past the last chip, or down over "Add Chord") still lands
            // at the end rather than needing to overlap the final chip.
            .contentShape(.rect)
            .coordinateSpace(.named(chordFlowCoordinateSpace))
            .dropDestination(for: String.self) { droppedIDs, location in
                handleChordDrop(droppedIDs, at: location)
            }
            .onChange(of: model.chords) { _, chords in
                // Drop frames for chords that no longer exist so the dictionary can't accumulate.
                let liveIDs = Set(chords.map(\.id))
                chipFrames = chipFrames.filter { liveIDs.contains($0.key) }
            }
        }
    }

    private var chordChipFlow: some View {
        ChipFlowLayout(spacing: 10) {
            ForEach(model.chords) { chord in
                ChordChip(chord: chord) {
                    withAnimation { model.removeChord(chord) }
                }
                .draggable(chord.id.uuidString)
                .onGeometryChange(for: CGRect.self) { proxy in
                    proxy.frame(in: .named(chordFlowCoordinateSpace))
                } action: { frame in
                    chipFrames[chord.id] = frame
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var chordsFooter: Text? {
        guard !model.chords.isEmpty else { return nil }
        let count = model.chords.count
        return Text("\(count) chord\(count == 1 ? "" : "s") — drag to reorder, tap \(Image(systemName: "xmark.circle.fill")) to remove.")
    }

    private var tempoCard: some View {
        SectionCard("Tempo") {
            VStack(alignment: .leading, spacing: 4) {
                Text("\(Int(model.tempo.rounded())) BPM")
                    .font(.title3)
                    .monospacedDigit()
                Slider(
                    value: $model.tempo,
                    in: GenerateTrackModel.tempoRange,
                    step: 1
                ) {
                    Text("Tempo")
                } minimumValueLabel: {
                    Text("40").font(.caption2)
                } maximumValueLabel: {
                    Text("240").font(.caption2)
                }
            }
            .padding(16)
        }
    }

    private var timeSignatureCard: some View {
        SectionCard(
            "Time Signature",
            footer: Text("Currently \(model.timeSignature.displayName).")
        ) {
            VStack(spacing: 0) {
                Toggle("Custom", isOn: $isCustomTimeSignature)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 11)
                    .onChange(of: isCustomTimeSignature) { _, isCustom in
                        // Leaving custom mode with a value that isn't a quick-pick would leave
                        // the menu below with nothing selected — snap back to 4/4.
                        if !isCustom && !model.timeSignature.isCommon {
                            model.timeSignature = .fourFour
                        }
                    }

                Divider().padding(.leading, 16)

                if isCustomTimeSignature {
                    CardRow("Beats per bar") {
                        Picker("Beats per bar", selection: $model.timeSignature.beatCount) {
                            ForEach(TimeSignature.allowedBeatCounts, id: \.self) { count in
                                Text("\(count)").tag(count)
                            }
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)
                    }

                    Divider().padding(.leading, 16)

                    CardRow("Beat note value") {
                        Picker("Beat note value", selection: $model.timeSignature.beatUnit) {
                            ForEach(TimeSignature.allowedBeatUnits, id: \.self) { unit in
                                Text("1/\(unit)").tag(unit)
                            }
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)
                    }
                } else {
                    CardRow("Time signature") {
                        Picker("Time signature", selection: $model.timeSignature) {
                            ForEach(TimeSignature.common, id: \.self) { signature in
                                Text(signature.displayName).tag(signature)
                            }
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)
                    }
                }
            }
        }
    }

    private var styleCard: some View {
        SectionCard("Style") {
            VStack(spacing: 0) {
                CardRow("Style") {
                    Picker("Style", selection: $model.selectedStyle) {
                        // The `nil` row is the initial state — there's no default style, so the
                        // user has to choose one before Generate enables. Tags must be
                        // `MusicStyle?` to match the optional selection binding.
                        Text("Select a style").tag(nil as MusicStyle?)
                        ForEach(MusicStyle.allCases) { style in
                            Text(style.displayName).tag(style as MusicStyle?)
                        }
                    }
                    .labelsHidden()
                    .pickerStyle(.menu)
                }

                Divider().padding(.leading, 16)

                // `axis: .vertical` lets the field grow to multiple lines as the user types.
                TextField("Notes (optional)", text: $model.styleNotes, axis: .vertical)
                    .lineLimit(2...4)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 11)
            }
        }
    }

    // MARK: - Actions

    /// Handles a chip dropped on the flow: resolves the dragged chord from its id payload and
    /// moves it to wherever the drop landed. Returns `false` for an unrecognised payload so the
    /// system animates the chip back.
    private func handleChordDrop(_ droppedIDs: [String], at location: CGPoint) -> Bool {
        guard let idString = droppedIDs.first,
              let draggedID = UUID(uuidString: idString),
              model.chords.contains(where: { $0.id == draggedID })
        else { return false }

        let targetIndex = ChipFlowGeometry.insertionIndex(
            for: location,
            orderedChipIDs: model.chords.map(\.id),
            frames: chipFrames
        )
        withAnimation(.snappy) {
            model.moveChord(withID: draggedID, toIndex: targetIndex)
        }
        return true
    }

}

/// The screen's sheets, alerts and the progress cover, grouped so `GenerateTrackView.body` stays
/// small enough for the type-checker. Takes the `@Binding`s it toggles plus the model it reads.
private struct GenerateTrackModals: ViewModifier {
    let model: GenerateTrackModel
    @Binding var isShowingSettings: Bool
    @Binding var isShowingAddChord: Bool
    @Binding var isShowingVibeComingSoon: Bool

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: $isShowingSettings) {
                SettingsView()
            }
            .sheet(isPresented: $isShowingAddChord) {
                AddChordView { root, quality in
                    model.addChord(root: root, quality: quality)
                }
                // Half-height sheet — the picker doesn't need the whole screen.
                .presentationDetents([.medium])
            }
            .alert("Vibe Mode Coming Soon", isPresented: $isShowingVibeComingSoon) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Vibe mode renders realistic audio from a text prompt. It isn't available yet — Precise mode for now.")
            }
            .alert(
                submissionAlert?.title ?? "",
                // Shown only for a submission *failure*; dismissing it clears that state
                // (`acknowledgeSubmissionResult`), returning to the form.
                isPresented: Binding(
                    get: { submissionAlert != nil },
                    set: { isPresented in if !isPresented { model.acknowledgeSubmissionResult() } }
                ),
                presenting: submissionAlert
            ) { _ in
                Button("OK", role: .cancel) {}
            } message: { alert in
                Text(alert.message)
            }
            // On acceptance, the progress screen takes over full-screen and polls the job. Its
            // `item` binding is `model.acceptedJob`; setting it back to `nil` (the screen calls
            // `dismiss()`) routes through `acknowledgeSubmissionResult()` → `.idle`, which also
            // ends the poll loop by tearing down the cover.
            .fullScreenCover(item: Binding(
                get: { model.acceptedJob },
                set: { newValue in if newValue == nil { model.acknowledgeSubmissionResult() } }
            )) { job in
                GenerationProgressView(model: model.makeProgressModel(for: job))
            }
    }

    /// The failure alert's contents, or `nil` otherwise. A *successful* submission routes to the
    /// `fullScreenCover` above, not an alert.
    private var submissionAlert: (title: String, message: String)? {
        switch model.submissionState {
        case .idle, .submitting, .submitted:
            nil
        case .failed(let error):
            ("Couldn't Generate Track", error.errorDescription ?? "Something went wrong.")
        }
    }
}

#Preview("Empty") {
    GenerateTrackView(model: GenerateTrackModel(historyStore: PreviewJobHistoryStore()))
}

#Preview("With progression") {
    let model = GenerateTrackModel(historyStore: PreviewJobHistoryStore())
    model.addChord(root: .c, quality: .major)
    model.addChord(root: .a, quality: .minor)
    model.addChord(root: .f, quality: .major)
    model.addChord(root: .g, quality: .dominant7)
    model.selectedStyle = .rock
    return GenerateTrackView(model: model)
}
