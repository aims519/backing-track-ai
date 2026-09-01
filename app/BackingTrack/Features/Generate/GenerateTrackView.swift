import SwiftUI

/// The app's main screen: choose the parameters of a backing track and request generation.
///
/// Precise mode only for now — Vibe mode's backend doesn't exist yet (see `GenerationMode`). The
/// actual network call and the generating → ready/failed flow are a separate task; `generate()`
/// here just shows what would be sent.
struct GenerateTrackView: View {
    // `@State` (not `@StateObject`) is how the current Observation model holds a reference type —
    // the view owns this instance for its lifetime and SwiftUI tracks reads into it. Injectable
    // (same idea as `SettingsView`'s store) so previews can seed a populated form.
    @State private var model: GenerateTrackModel

    init(model: GenerateTrackModel = GenerateTrackModel()) {
        // `State(initialValue:)` is the supported way to seed `@State` from an initializer; it's
        // used only on the view's first construction, not on later re-inits.
        _model = State(initialValue: model)
    }

    @State private var isShowingSettings = false
    @State private var isShowingAddChord = false
    @State private var isShowingSubmitConfirmation = false
    @State private var isShowingVibeComingSoon = false
    // Toggling this reveals the custom beat-count / beat-unit pickers instead of the quick-pick
    // list. Kept in the view (not the model) — it's a display choice, not part of the request.
    @State private var isCustomTimeSignature = false

    var body: some View {
        NavigationStack {
            Form {
                modeSection
                keySection
                chordsSection
                tempoSection
                timeSignatureSection
                styleSection
            }
            .navigationTitle("New Track")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    // Reordering needs edit mode; only worth offering with more than one chord.
                    // Swipe-to-delete works without it.
                    if model.chords.count > 1 {
                        EditButton()
                    }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        isShowingSettings = true
                    } label: {
                        Label("Settings", systemImage: "gearshape")
                    }
                }
            }
            // Primary action pinned above the keyboard/home indicator rather than buried in the
            // scrolling form.
            .safeAreaInset(edge: .bottom) {
                Button {
                    generate()
                } label: {
                    Text("Generate Track")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(!model.canGenerate)
                .padding()
                .background(.bar)
            }
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
                "Track Requested",
                isPresented: $isShowingSubmitConfirmation,
                presenting: model.lastSubmission
            ) { _ in
                Button("OK", role: .cancel) {}
            } message: { request in
                Text(confirmationMessage(for: request))
            }
        }
    }

    // MARK: - Sections

    private var modeSection: some View {
        Section {
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
        } footer: {
            Text(model.mode.summary)
        }
    }

    private var keySection: some View {
        Section {
            Picker("Key Root", selection: $model.keyRoot) {
                ForEach(NoteName.allCases) { note in
                    Text(note.displayName).tag(note)
                }
            }
            .pickerStyle(.menu)
        } header: {
            Text("Key Root")
        } footer: {
            Text("The tonal centre. Major or minor isn't sent separately — your chords imply it.")
        }
    }

    private var chordsSection: some View {
        Section {
            if model.chords.isEmpty {
                Text("Add at least one chord to build the progression.")
                    .foregroundStyle(.secondary)
            } else {
                ForEach(model.chords) { chord in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(chord.displaySymbol)
                            .font(.headline)
                        Text("\(chord.root.displayName) \(chord.quality.pickerName)")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .onMove(perform: model.moveChords)
                .onDelete(perform: model.removeChords)
            }

            Button {
                isShowingAddChord = true
            } label: {
                Label("Add Chord", systemImage: "plus")
            }
        } header: {
            Text("Chords")
        } footer: {
            if !model.chords.isEmpty {
                let count = model.chords.count
                Text("\(count) chord\(count == 1 ? "" : "s"), played in order.")
            }
        }
    }

    private var tempoSection: some View {
        Section {
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
        } header: {
            Text("Tempo")
        }
    }

    private var timeSignatureSection: some View {
        Section {
            Toggle("Custom", isOn: $isCustomTimeSignature)
                .onChange(of: isCustomTimeSignature) { _, isCustom in
                    // Leaving custom mode with a value that isn't a quick-pick would leave the
                    // menu below with nothing selected — snap back to 4/4.
                    if !isCustom && !model.timeSignature.isCommon {
                        model.timeSignature = .fourFour
                    }
                }

            if isCustomTimeSignature {
                Picker("Beats per bar", selection: $model.timeSignature.beatCount) {
                    ForEach(TimeSignature.allowedBeatCounts, id: \.self) { count in
                        Text("\(count)").tag(count)
                    }
                }
                Picker("Beat note value", selection: $model.timeSignature.beatUnit) {
                    ForEach(TimeSignature.allowedBeatUnits, id: \.self) { unit in
                        Text("1/\(unit)").tag(unit)
                    }
                }
            } else {
                Picker("Time signature", selection: $model.timeSignature) {
                    ForEach(TimeSignature.common, id: \.self) { signature in
                        Text(signature.displayName).tag(signature)
                    }
                }
                .pickerStyle(.menu)
            }
        } header: {
            Text("Time Signature")
        } footer: {
            Text("Currently \(model.timeSignature.displayName).")
        }
    }

    private var styleSection: some View {
        Section {
            Picker("Style", selection: $model.selectedStyle) {
                // The `nil` row is the initial state — there's no default style, so the user has
                // to choose one before Generate enables. Tags must be `MusicStyle?` to match the
                // optional selection binding.
                Text("Select a style").tag(nil as MusicStyle?)
                ForEach(MusicStyle.allCases) { style in
                    Text(style.displayName).tag(style as MusicStyle?)
                }
            }
            .pickerStyle(.menu)

            // `axis: .vertical` lets the field grow to multiple lines as the user types.
            TextField("Notes (optional)", text: $model.styleNotes, axis: .vertical)
                .lineLimit(2...4)
        } header: {
            Text("Style")
        }
    }

    // MARK: - Actions

    private func generate() {
        if model.submit() != nil {
            isShowingSubmitConfirmation = true
        }
    }

    private func confirmationMessage(for request: GenerationRequest) -> String {
        """
        \(request.mode) · \(request.tempo) BPM · \(request.timeSignatureBeatCount)/\(request.timeSignatureBeatUnit) · Key of \(request.keyRoot)

        Chords: \(request.chords.joined(separator: " – "))

        Style: \(request.style)

        Networking isn't wired up yet — this is what would be sent to /tracks/generate.
        """
    }
}

#Preview("Empty") {
    GenerateTrackView()
}

#Preview("With progression") {
    let model = GenerateTrackModel()
    model.addChord(root: .c, quality: .major)
    model.addChord(root: .a, quality: .minor)
    model.addChord(root: .f, quality: .major)
    model.addChord(root: .g, quality: .dominant7)
    model.selectedStyle = .rock
    return GenerateTrackView(model: model)
}
