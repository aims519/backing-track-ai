import AVFAudio

/// Turns `AVAudioSession` interruption and route-change notifications into callbacks the engine
/// wires up. Block-based `addObserver` rather than the async `.notifications(named:)` sequence —
/// it's the documented way to observe these and delivers reliably.
///
/// The notifications arrive on an arbitrary thread; the blocks parse the `Sendable` primitives
/// out and hop to the main actor to fire the callbacks.
@MainActor
final class AudioSessionController {
    var onInterruptionBegan: (() -> Void)?
    /// The interruption ended and the system says playback may resume.
    var onShouldResume: (() -> Void)?
    /// The output route the audio was going to went away (headphones unplugged, Bluetooth lost).
    var onRouteGone: (() -> Void)?

    private var observers: [NSObjectProtocol] = []

    func startObserving() {
        guard observers.isEmpty else { return }
        let center = NotificationCenter.default

        observers.append(center.addObserver(
            forName: AVAudioSession.interruptionNotification, object: nil, queue: nil
        ) { [weak self] note in
            guard let raw = note.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
                  let type = AVAudioSession.InterruptionType(rawValue: raw)
            else { return }
            let shouldResume = type == .ended
                && AVAudioSession.InterruptionOptions(
                    rawValue: note.userInfo?[AVAudioSessionInterruptionOptionKey] as? UInt ?? 0
                ).contains(.shouldResume)

            Task { @MainActor [weak self] in
                switch type {
                case .began: self?.onInterruptionBegan?()
                case .ended: if shouldResume { self?.onShouldResume?() }
                @unknown default: break
                }
            }
        })

        observers.append(center.addObserver(
            forName: AVAudioSession.routeChangeNotification, object: nil, queue: nil
        ) { [weak self] note in
            guard let raw = note.userInfo?[AVAudioSessionRouteChangeReasonKey] as? UInt,
                  AVAudioSession.RouteChangeReason(rawValue: raw) == .oldDeviceUnavailable
            else { return }
            Task { @MainActor [weak self] in self?.onRouteGone?() }
        })
    }

    func stopObserving() {
        observers.forEach(NotificationCenter.default.removeObserver)
        observers.removeAll()
    }
}
