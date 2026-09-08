import MediaPlayer

/// Drives the system Now Playing surface — lock screen and Control Center — for the player
/// screen: the track's title/subtitle, a progress bar over one loop, and the remote play / pause
/// commands wired back to the view model.
///
/// Owned by `PlayerViewModel`: created once the track loads, torn down when the screen goes away
/// (`teardown()` clears the info and removes the command targets, so a stale lock-screen entry
/// doesn't linger). A protocol so the view model's tests don't touch the `MPRemoteCommandCenter`
/// process singleton.
///
/// Only discrete `play` / `pause` are registered — not `togglePlayPause`. With all three, the
/// Control Center button double-fires (pause, then toggle undoes it), so the button snaps back to
/// "playing"; modern iOS routes the CC and headset buttons through the discrete commands anyway.
@MainActor
protocol NowPlayingControlling: AnyObject {
    var onPlay: (() -> Void)? { get set }
    var onPause: (() -> Void)? { get set }

    /// Pushes the current position and playing state. Call on play / pause, on a tempo change
    /// (the loop's wall-clock length changes), and on each loop wrap; the system extrapolates the
    /// elapsed time between calls from the rate.
    func update(elapsedSeconds: Double, loopDurationSeconds: Double, isPlaying: Bool)

    func teardown()
}

@MainActor
final class NowPlayingController: NowPlayingControlling {
    var onPlay: (() -> Void)?
    var onPause: (() -> Void)?

    private let title: String
    private let subtitle: String
    private var commandTokens: [(command: MPRemoteCommand, token: Any)] = []

    init(title: String, subtitle: String) {
        self.title = title
        self.subtitle = subtitle
        registerCommands()
        update(elapsedSeconds: 0, loopDurationSeconds: 0, isPlaying: false)
    }

    func update(elapsedSeconds: Double, loopDurationSeconds: Double, isPlaying: Bool) {
        var info: [String: Any] = [
            MPMediaItemPropertyTitle: title,
            MPMediaItemPropertyArtist: subtitle,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: elapsedSeconds,
            MPNowPlayingInfoPropertyPlaybackRate: isPlaying ? 1.0 : 0.0,
        ]
        // Only advertise a duration once we know it — a 0 would draw a full, stuck scrubber.
        if loopDurationSeconds > 0 {
            info[MPMediaItemPropertyPlaybackDuration] = loopDurationSeconds
        }
        // iOS infers the playing state from the reported rate plus the live audio output —
        // MPNowPlayingInfoCenter.playbackState needs a private entitlement and is ignored here.
        // So the engine must actually stop the audio on pause for the state to read as paused.
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    func teardown() {
        for (command, token) in commandTokens { command.removeTarget(token) }
        commandTokens.removeAll()
        MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
    }

    private func registerCommands() {
        let center = MPRemoteCommandCenter.shared()

        add(center.playCommand) { [weak self] in self?.onPlay?() }
        add(center.pauseCommand) { [weak self] in self?.onPause?() }

        // A backing track loops forever, and toggle double-fires with pause (see the type doc).
        for unsupported in [
            center.togglePlayPauseCommand,
            center.nextTrackCommand, center.previousTrackCommand,
            center.seekForwardCommand, center.seekBackwardCommand,
            center.skipForwardCommand, center.skipBackwardCommand,
            center.changePlaybackPositionCommand,
        ] {
            unsupported.isEnabled = false
        }
    }

    private func add(_ command: MPRemoteCommand, _ handler: @escaping @MainActor () -> Void) {
        // A remote-command handler can fire off the main thread when the app is backgrounded, so
        // hop explicitly rather than asserting isolation (which a release build would ignore,
        // then race on the view model's main-actor state).
        let token = command.addTarget { _ in
            Task { @MainActor in handler() }
            return .success
        }
        command.isEnabled = true
        commandTokens.append((command, token))
    }
}
