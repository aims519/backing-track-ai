import AVFAudio

@MainActor
final class AudioSessionController {
    var onInterruptionBegan: (() -> Void)?
    var onShouldResume: (() -> Void)?
    var onRouteGone: (() -> Void)?
    
    func observeInterruptions() async {
        for await notification in NotificationCenter.default.notifications(
            named: AVAudioSession.interruptionNotification,
            object: AVAudioSession.sharedInstance()
        ) {
            handleInterruption(notification: notification)
        }
    }
    
    func observeRouteChanges() async {
        for await notification in NotificationCenter.default.notifications(
            named: AVAudioSession.routeChangeNotification,
            object: AVAudioSession.sharedInstance()
        ) {
            handleRouteChange(notification: notification)
        }
    }
    
    private func handleInterruption(notification: Notification) {
        guard let userInfo = notification.userInfo,
            let typeValue = userInfo[AVAudioSessionInterruptionTypeKey] as? UInt,
            let type = AVAudioSession.InterruptionType(rawValue: typeValue) else {
                return
        }
        
        switch type {
            case .began:
                onInterruptionBegan?()
            case .ended:
                guard let optionsValue = userInfo[AVAudioSessionInterruptionOptionKey] as? UInt else { return }
                let options = AVAudioSession.InterruptionOptions(rawValue: optionsValue)
                if options.contains(.shouldResume) {
                    // An interruption ended. Resume playback
                    onShouldResume?()
                }
            @unknown default: ()
        }
    }
    
    private func handleRouteChange(notification: Notification) {
        guard let userInfo = notification.userInfo,
            let reasonValue = userInfo[AVAudioSessionRouteChangeReasonKey] as? UInt,
            let reason = AVAudioSession.RouteChangeReason(rawValue: reasonValue) else {
                return
        }
        
        if reason == .oldDeviceUnavailable {
            onRouteGone?()
        }    
    }
}
