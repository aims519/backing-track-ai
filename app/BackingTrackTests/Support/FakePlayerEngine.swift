import Foundation
@testable import BackingTrack

/// A `TrackPlaying` that records calls and lets a test drive `state` / `currentBeat`, so
/// `PlayerViewModelTests` can exercise the model without an `AVAudioEngine`.
@MainActor
final class FakePlayerEngine: TrackPlaying {
    private(set) var state: PlaybackState = .idle {
        didSet { onStateChange?(state) }
    }
    var onStateChange: ((PlaybackState) -> Void)?
    var currentBeat: Double = 0
    var isCountInEnabled = false

    private(set) var playCalls = 0
    private(set) var pauseCalls = 0
    private(set) var stopCalls = 0
    private(set) var teardownCalls = 0
    private(set) var tempoUpdates: [Double] = []
    private(set) var mutes: [(muted: Bool, voice: PlaybackVoice)] = []
    private(set) var volumes: [(volume: Float, voice: PlaybackVoice)] = []

    func play() { playCalls += 1; state = .playing }
    func pause() { pauseCalls += 1; state = .paused }
    func stop() { stopCalls += 1; state = .idle }
    func teardown() { teardownCalls += 1 }
    func updateTempo(newTempo: Double) { tempoUpdates.append(newTempo) }
    func setVoiceVolume(_ volume: Float, for voice: PlaybackVoice) { volumes.append((volume, voice)) }
    func setMuted(_ muted: Bool, for voice: PlaybackVoice) { mutes.append((muted, voice)) }

    /// Drives `state` directly (and fires `onStateChange`) — for simulating an interruption pause.
    func simulateStateChange(to newState: PlaybackState) { state = newState }
}
