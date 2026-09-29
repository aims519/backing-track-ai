import SwiftUI

/// The player screen: loads a completed job's backing track and plays it on a loop, with live
/// tempo, a current-chord highlight and per-instrument mixing.
///
/// Reached by a navigation push from the progress screen's "Ready" state and from a history
/// row's detail. Laid out as a `ScrollView` of `SectionCard`s to match Progress / History detail.
struct PlayerView: View {
    @State private var model: PlayerViewModel

    init(model: PlayerViewModel) {
        _model = State(initialValue: model)
    }

    var body: some View {
        content
            .navigationTitle(navigationTitle)
            .navigationBarTitleDisplayMode(.inline)
            .background(Color(.systemGroupedBackground))
            .task { await model.load() }
            .onDisappear { model.teardown() }
    }

    @ViewBuilder
    private var content: some View {
        switch model.phase {
        case .loading:
            loadingView
        case .failed(let error):
            failureView(error)
        case .loaded(let track):
            loadedView(track)
        }
    }

    // MARK: - Loading / failure

    private var loadingView: some View {
        VStack(spacing: 12) {
            ProgressView()
            Text("Loading track…").foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func failureView(_ error: TrackLoadError) -> some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)
                .accessibilityHidden(true)

            Text("Can't play this track")
                .font(.title3.weight(.semibold))
                .multilineTextAlignment(.center)

            Text(failureMessage(error))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            if error.isRetryable {
                Button("Try again") { Task { await model.retry() } }
                    .buttonStyle(.bordered)
            }
        }
        .padding(.horizontal, 24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Loaded

    private func loadedView(_ track: PlayableTrack) -> some View {
        ScrollView {
            VStack(spacing: 22) {
                styleNotesLine
                chordsCard(track)
                transportCard(track)
                tempoCard
                instrumentsCard
            }
            .padding(.vertical, 16)
        }
        // Starts when the loaded view appears, cancelled when it goes away (load failure, dismiss).
        .task { await model.trackPlayHead() }
    }

    /// Free-form style notes shape how the player plays over the track, so they sit above the
    /// chords. Too long for the nav title, which carries the style name, key and time.
    @ViewBuilder
    private var styleNotesLine: some View {
        if !styleNotes.isEmpty {
            Text(styleNotes)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .padding(.horizontal, 20)
        }
    }

    private func transportCard(_ track: PlayableTrack) -> some View {
        SectionCard("Playback") {
            VStack(spacing: 14) {
                HStack(spacing: 10) {
                    Button {
                        model.playPause()
                    } label: {
                        Label(playButtonTitle, systemImage: playButtonIcon)
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)

                    Button {
                        model.stop()
                    } label: {
                        Label("Stop", systemImage: "stop.fill")
                    }
                    .buttonStyle(.bordered)
                    .disabled(model.playbackState == .idle)
                }
                .controlSize(.large)

                Text("Bar \(model.currentBar + 1) of \(track.barCount)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()

                Toggle("Count in", isOn: Binding(
                    get: { model.isCountInEnabled },
                    set: { model.setCountIn($0) }
                ))
                .disabled(model.playbackState == .playing || model.playbackState == .countingIn)

                if case .failed(let message) = model.playbackState {
                    Text(message)
                        .font(.footnote)
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.center)
                }
            }
            .padding(16)
        }
    }

    private var tempoCard: some View {
        SectionCard("Tempo") {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("\(model.tempoBpm) BPM")
                        .font(.title3)
                        .monospacedDigit()
                        // The slider's tick marks the original visually; VoiceOver gets it here.
                        .accessibilityLabel("\(model.tempoBpm) BPM, original \(model.originalTempoBpm) BPM")
                    Spacer()
                    if model.tempoBpm != model.originalTempoBpm {
                        Button("Reset") { model.resetTempo() }
                            .font(.subheadline)
                    }
                }
                tempoSlider
            }
            .padding(16)
        }
        .sensoryFeedback(.selection, trigger: model.tempoBpm) { _, new in
            new == model.originalTempoBpm
        }
    }

    /// A marker under the slider at the original tempo. Hand-drawn because `Slider`'s native ticks
    /// (iOS 26+) didn't render visibly, and it has to work on iOS 18 too.
    private var tempoSlider: some View {
        VStack(spacing: 2) {
            Slider(
                value: Binding(get: { Double(model.tempoBpm) }, set: { model.dragTempo($0) }),
                in: Tempo.range,
                step: 1
            )
            GeometryReader { geometry in
                Capsule()
                    .fill(Color.secondary)
                    .frame(width: 2, height: 8)
                    .position(x: originalTempoMarkerX(in: geometry.size.width), y: 4)
            }
            .frame(height: 8)
            .accessibilityHidden(true)
        }
    }

    /// The thumb's centre stops short of each end of the track by half the thumb's width, so the
    /// marker is mapped onto that narrower span. `Slider` doesn't expose its thumb size; this is
    /// tuned by eye (thumb resting on the original at load should sit centred over the marker).
    private static let sliderThumbInset: CGFloat = 14

    private func originalTempoMarkerX(in width: CGFloat) -> CGFloat {
        let range = Tempo.range
        let fraction = (Double(model.originalTempoBpm) - range.lowerBound) / (range.upperBound - range.lowerBound)
        let inset = Self.sliderThumbInset
        return inset + CGFloat(fraction) * (width - 2 * inset)
    }

    private func chordsCard(_ track: PlayableTrack) -> some View {
        SectionCard("Chords") {
            VStack(spacing: 0) {
                LiveCurrentChord(model: model, track: track)
                Divider().padding(.leading, 16)
                ChordProgressionStrip(symbols: track.chordSymbols, currentBar: model.currentBar)
            }
        }
    }

    private var instrumentsCard: some View {
        SectionCard("Instruments") {
            VStack(spacing: 0) {
                ForEach(Array(PlaybackVoice.allCases.enumerated()), id: \.element) { index, voice in
                    if index > 0 { Divider().padding(.leading, 16) }
                    voiceRow(voice)
                }
            }
        }
    }

    private func voiceRow(_ voice: PlaybackVoice) -> some View {
        HStack(spacing: 12) {
            Button {
                model.setMuted(!model.isMuted(voice), for: voice)
            } label: {
                Image(systemName: model.isMuted(voice) ? "speaker.slash.fill" : "speaker.wave.2.fill")
                    .frame(width: 24)
            }
            .buttonStyle(.plain)
            .foregroundStyle(model.isMuted(voice) ? Color.secondary : Color.accentColor)
            .accessibilityLabel("\(voice.rawValue) \(model.isMuted(voice) ? "muted" : "playing")")

            Text(voice.rawValue)
                .frame(width: 56, alignment: .leading)

            Slider(
                value: Binding(
                    get: { model.volume(voice) },
                    set: { model.setVolume($0, for: voice) }
                ),
                in: 0...1
            )
            .disabled(model.isMuted(voice))
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }

    // MARK: - Derived

    /// "Blues · C Ionian · 4/4". Built from the input rather than the loaded track so it's right
    /// while loading too. The style is left out when it isn't one we recognise; `styleNotes`
    /// then shows the whole stored string instead.
    private var navigationTitle: String {
        let input = model.input
        let key = "\(NoteName(rawValue: input.keyRoot)?.displayName ?? input.keyRoot) \(input.keyMode)"
        let style = decomposeStyle(input.title ?? "").0?.displayName
        return [style, key, input.timeSignature.displayName].compactMap { $0 }.joined(separator: " · ")
    }

    private var playButtonTitle: String {
        switch model.playbackState {
        case .playing: "Pause"
        case .countingIn: "Counting in…"
        default: "Play"
        }
    }

    private var playButtonIcon: String {
        switch model.playbackState {
        case .playing: "pause.fill"
        case .countingIn: "metronome.fill"
        default: "play.fill"
        }
    }

    /// Whatever `navigationTitle` doesn't show: the notes after a known style, or the whole
    /// string when the style isn't one we recognise (the title then falls back to "Player").
    private var styleNotes: String {
        let stored = model.input.title ?? ""
        let (style, notes) = decomposeStyle(stored)
        return style == nil ? stored : notes
    }

    private func failureMessage(_ error: TrackLoadError) -> String {
        switch error {
        case .download(let apiError):
            apiError.errorDescription ?? "Couldn't download the track. Check your connection and try again."
        case .decode:
            "The downloaded track was in a format the app couldn't read."
        case .notPrecise:
            "This is a Vibe-mode track. Playback for those isn't available yet."
        }
    }
}

/// Reads the beat in its own body so only this view redraws on each beat, not the whole screen.
private struct LiveCurrentChord: View {
    let model: PlayerViewModel
    let track: PlayableTrack

    var body: some View {
        CurrentChordView(
            symbols: track.chordSymbols,
            currentBar: model.currentBar,
            currentBeat: (model.playbackState == .playing || model.playbackState == .paused) ? model.currentBeatInBar : nil,
            beatsInBar: track.beatsInBar
        )
    }
}

private extension TrackLoadError {
    /// A format problem and a Vibe track won't fix themselves on a retry; a download failure might.
    var isRetryable: Bool {
        if case .download = self { return true }
        return false
    }
}

#if DEBUG
#Preview("Loaded") {
    NavigationStack {
        PlayerView(model: .preview(.loaded(.preview()), currentBar: 1))
    }
}

#Preview("Playing") {
    NavigationStack {
        PlayerView(model: .preview(.loaded(.preview()), playbackState: .playing, currentBar: 2))
    }
}

#Preview("Loading") {
    NavigationStack {
        PlayerView(model: .preview(.loading))
    }
}

#Preview("Failed — offline") {
    NavigationStack {
        PlayerView(model: .preview(.failed(.download(.transport(URLError(.notConnectedToInternet))))))
    }
}
#endif
