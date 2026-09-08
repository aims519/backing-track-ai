import Foundation

/// What `PlayerViewModel` needs from the audio engine. An abstraction so the view model can be
/// unit-tested without an `AVAudioEngine`: `AudioEngine` is the only production conformer,
/// `FakePlayerEngine` stands in for tests and previews.
///
/// The engine is created *after* the track loads (`init(playableTrack:)`), so the view model
/// takes a `makeEngine` factory rather than an instance.
@MainActor
protocol TrackPlaying: AnyObject {
    var state: PlaybackState { get }
    /// Fires on every transition, including engine-driven ones (interruption, route change) the
    /// view model didn't ask for.
    var onStateChange: ((PlaybackState) -> Void)? { get set }
    /// Sequencer play head in beats. Climbs past the loop point without wrapping (see
    /// [[project_sequencer_position_climbs]] / plan Risk 1) — callers reduce it into the loop.
    var currentBeat: Double { get }

    /// One bar of clicks before playback starts. Takes effect on the next start from idle.
    var isCountInEnabled: Bool { get set }

    func play()
    func pause()
    func stop()
    func teardown()

    func updateTempo(newTempo: Double)
    func setVoiceVolume(_ volume: Float, for voice: PlaybackVoice)
    func setMuted(_ muted: Bool, for voice: PlaybackVoice)
}

extension AudioEngine: TrackPlaying {}
