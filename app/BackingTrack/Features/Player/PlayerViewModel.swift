import Observation
import SwiftUI

/// Backing state for `PlayerView`: loads a completed job's note-event track (cache first, then
/// download), assembles it into a `PlayableTrack`, hands that to an audio engine, and exposes the
/// transport / tempo / mixer controls the screen binds to.
///
/// The engine is behind the `TrackPlaying` protocol and built through a `makeEngine` factory —
/// it can't exist until the track is loaded, and tests swap in `FakePlayerEngine`. Engine state
/// (`playbackState`) and play head (`currentBar`) are *mirrored* into this model's own observable
/// properties: an existential `any TrackPlaying` doesn't propagate `@Observable` tracking, so the
/// view watches this model, not the engine.
@MainActor
@Observable
final class PlayerViewModel {

    /// Where the screen is in getting a track ready to play. `.loading` is the only non-terminal
    /// case; `retry()` re-runs from `.failed`.
    enum LoadPhase {
        case loading
        case loaded(PlayableTrack)
        case failed(TrackLoadError)
    }

    let input: PlayerInput

    private(set) var phase: LoadPhase = .loading
    /// Mirror of the engine's transport state — updated via the engine's `onStateChange`, so it
    /// also reflects interruption / route-change pauses the user didn't trigger.
    private(set) var playbackState: PlaybackState = .idle
    /// Current bar in the loop (0-based), derived from the sequencer play head while playing.
    private(set) var currentBar: Int = 0
    private(set) var tempoBpm: Int
    /// One bar of clicks before playback starts. Applied to the engine on the next start.
    private(set) var isCountInEnabled = false

    /// Per-voice mixer state. The view's source of truth; forwarded to the engine, which keeps
    /// its own copy for the audio graph.
    private(set) var mutedVoices: Set<PlaybackVoice> = []
    private(set) var voiceVolumes: [PlaybackVoice: Float] = [.drums: 1, .bass: 1, .chords: 1]

    @ObservationIgnored private let loader: TrackLoader
    @ObservationIgnored private let makeEngine: @MainActor (PlayableTrack) -> any TrackPlaying
    @ObservationIgnored private let makeNowPlaying: @MainActor (_ title: String, _ subtitle: String) -> any NowPlayingControlling
    @ObservationIgnored private var engine: (any TrackPlaying)?
    @ObservationIgnored private var nowPlaying: (any NowPlayingControlling)?

    init(
        input: PlayerInput,
        apiClient: any BackingTrackAPIClient = LiveBackingTrackAPIClient(),
        historyStore: any JobHistoryStore = SwiftDataJobHistoryStore(),
        makeEngine: @escaping @MainActor (PlayableTrack) -> any TrackPlaying = { AudioEngine(playableTrack: $0) },
        makeNowPlaying: @escaping @MainActor (String, String) -> any NowPlayingControlling = { NowPlayingController(title: $0, subtitle: $1) }
    ) {
        self.input = input
        self.tempoBpm = input.tempo
        self.loader = TrackLoader(apiClient: apiClient, historyStore: historyStore)
        self.makeEngine = makeEngine
        self.makeNowPlaying = makeNowPlaying
    }

    var originalTempoBpm: Int { input.tempo }

    // MARK: - Loading

    /// Run from the view's `.task`. Loads the track, builds the engine, lands in `.loaded` or
    /// `.failed`. A no-op once loading has finished (or on a preview model parked in a phase) —
    /// `retry()` is the way back in from `.failed`.
    func load() async {
        guard case .loading = phase else { return }
        await performLoad()
    }

    func retry() async {
        phase = .loading
        await performLoad()
    }

    private func performLoad() async {
        guard input.isPrecise else {
            phase = .failed(.notPrecise)
            return
        }

        switch await loader.load(trackID: input.trackID, jobID: input.jobID) {
        case .failure(let error):
            phase = .failed(error)

        case .success(let candidate):
            let playable = PlayableTrack(
                trackID: input.trackID,
                track: candidate,
                originalTempo: input.tempo,
                keyRoot: NoteName(rawValue: input.keyRoot),
                timeSignature: input.timeSignature,
                chordSymbols: input.chordSymbols,
                title: input.title
            )

            let engine = makeEngine(playable)
            engine.onStateChange = { [weak self] newState in
                guard let self else { return }
                self.playbackState = newState
                self.syncNowPlaying()
            }
            engine.updateTempo(newTempo: Double(tempoBpm))
            engine.isCountInEnabled = isCountInEnabled
            self.engine = engine
            self.playbackState = engine.state

            let nowPlaying = makeNowPlaying(input.title ?? "Backing Track", nowPlayingSubtitle(playable))
            nowPlaying.onPlay = { [weak self] in self?.play() }
            nowPlaying.onPause = { [weak self] in self?.pause() }
            self.nowPlaying = nowPlaying

            self.phase = .loaded(playable)
            syncNowPlaying()
        }
    }

    /// `Key · chord chord chord` for the lock-screen subtitle.
    private func nowPlayingSubtitle(_ playable: PlayableTrack) -> String {
        let key = playable.keyRoot?.displayName ?? input.keyRoot
        let chords = playable.chordSymbols.prefix(8).joined(separator: " ")
        return chords.isEmpty ? key : "\(key) · \(chords)"
    }

    // MARK: - Transport

    func playPause() {
        guard let engine else { return }
        switch engine.state {
        case .playing, .countingIn: engine.pause()
        default: engine.play()
        }
    }

    /// Back to the top and idle — the way to re-arm the count-in (which only fires from idle).
    /// The play-head poll doesn't run while idle, so snap the chord highlight back here.
    func stop() {
        engine?.stop()
        currentBar = 0
    }

    func setCountIn(_ enabled: Bool) {
        isCountInEnabled = enabled
        engine?.isCountInEnabled = enabled
    }

    /// Remote-command entry points (lock screen / Control Center) — unconditional, unlike the
    /// UI's single toggle.
    func play() {
        guard let engine, engine.state != .playing else { return }
        engine.play()
    }

    func pause() {
        guard let engine, engine.state == .playing else { return }
        engine.pause()
    }

    // MARK: - Tempo

    func setTempo(_ bpm: Double) {
        let bounds = Tempo.range
        tempoBpm = min(max(Int(bpm.rounded()), Int(bounds.lowerBound)), Int(bounds.upperBound))
        engine?.updateTempo(newTempo: Double(tempoBpm))
        syncNowPlaying()
    }

    func resetTempo() { setTempo(Double(input.tempo)) }

    // MARK: - Mixer

    func isMuted(_ voice: PlaybackVoice) -> Bool { mutedVoices.contains(voice) }

    func volume(_ voice: PlaybackVoice) -> Float { voiceVolumes[voice] ?? 1 }

    func setMuted(_ muted: Bool, for voice: PlaybackVoice) {
        if muted { mutedVoices.insert(voice) } else { mutedVoices.remove(voice) }
        engine?.setMuted(muted, for: voice)
    }

    func setVolume(_ volume: Float, for voice: PlaybackVoice) {
        voiceVolumes[voice] = volume
        engine?.setVoiceVolume(volume, for: voice)
    }

    // MARK: - Play head

    /// Run from the view's `.task` while `.loaded`. Polls the sequencer play head (which climbs
    /// forever — plan Risk 1), reduces it into the loop, and publishes the current bar for the
    /// chord-progression highlight. Cancelled when the loaded view goes away.
    ///
    /// `currentBar` is only assigned when it actually changes: `@Observable` notifies on every
    /// set, and re-rendering the whole screen ~7×/second for an unchanged value is wasted main-
    /// thread work that can disturb playback.
    func trackPlayHead() async {
        guard case .loaded(let playable) = phase, let engine else { return }
        let loopLength = playable.track.lengthInBeats
        guard loopLength > 0 else { return }

        while !Task.isCancelled {
            // Reading currentPositionInBeats contends with the sequencer's timing thread, so only
            // do it while it matters (playing) and no faster than the highlight needs.
            if playbackState == .playing {
                let loopBeat = engine.currentBeat.truncatingRemainder(dividingBy: loopLength)
                let bar = playable.bar(atLoopBeat: loopBeat)
                if bar != currentBar {
                    let wrapped = bar < currentBar
                    currentBar = bar
                    // Re-sync the lock-screen scrubber when the loop restarts; the system
                    // extrapolates position from the rate the rest of the time.
                    if wrapped { syncNowPlaying() }
                }
            }
            try? await Task.sleep(for: .milliseconds(250))
        }
    }

    /// Pushes the current position to the Now Playing surface. Called on transport / tempo
    /// changes and each loop wrap.
    private func syncNowPlaying() {
        guard case .loaded(let playable) = phase, let engine, let nowPlaying else { return }
        let loopBeats = playable.track.lengthInBeats
        let tempo = Double(tempoBpm)
        let loopBeat = loopBeats > 0 ? engine.currentBeat.truncatingRemainder(dividingBy: loopBeats) : 0
        nowPlaying.update(
            elapsedSeconds: tempo > 0 ? loopBeat / tempo * 60 : 0,
            loopDurationSeconds: playable.loopDuration(atTempo: tempo),
            isPlaying: playbackState == .playing
        )
    }

    // MARK: - Teardown

    /// From the view's `.onDisappear` — releases the engine's process-wide audio resources and
    /// clears the lock-screen entry.
    func teardown() {
        engine?.teardown()
        nowPlaying?.teardown()
    }
}

#if DEBUG
extension PlayerViewModel {
    /// A model parked in a given phase for `#Preview`s. No engine — previews are for layout, and
    /// none should touch `AVAudioEngine`.
    static func preview(
        _ phase: LoadPhase = .loaded(.preview()),
        playbackState: PlaybackState = .idle,
        currentBar: Int = 0
    ) -> PlayerViewModel {
        // load() is never called on a preview model (phase is set below), so the API client is
        // never touched — the default is fine; the store is the in-memory preview one.
        let model = PlayerViewModel(input: .preview(), historyStore: PreviewJobHistoryStore())
        model.phase = phase
        model.playbackState = playbackState
        model.currentBar = currentBar
        return model
    }
}
#endif
