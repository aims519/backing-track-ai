import Foundation
@testable import BackingTrack

/// A `NowPlayingControlling` that records calls instead of touching `MPRemoteCommandCenter` /
/// `MPNowPlayingInfoCenter`, so `PlayerViewModelTests` can assert the lock-screen wiring without
/// mutating process-wide media state.
@MainActor
final class FakeNowPlaying: NowPlayingControlling {
    var onPlay: (() -> Void)?
    var onPause: (() -> Void)?

    private(set) var updates: [(elapsed: Double, duration: Double, isPlaying: Bool)] = []
    private(set) var teardownCalls = 0

    func update(elapsedSeconds: Double, loopDurationSeconds: Double, isPlaying: Bool) {
        updates.append((elapsedSeconds, loopDurationSeconds, isPlaying))
    }

    func teardown() { teardownCalls += 1 }
}
