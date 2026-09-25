import os
import UIKit

final class AppDelegate: NSObject, UIApplicationDelegate {
    private let log = Logger(subsystem: "com.andrewsavory.backingtrack", category: "push")
    
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions:[UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // Registering doesn't prompt the user; it only fetches a token. Whether alerts are
        // actually shown is the separate notification-permission request.
        application.registerForRemoteNotifications()
        return true
    }
    
    func application(_ application: UIApplication,didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        // The API expects hex (validated in GenerationRequestDto); APNs hands us raw bytes.
        let token = deviceToken.map { String(format: "%02x", $0) }.joined()
        PushTokenStore.deviceToken = token
        log.info("APNs device token received: \(token)")
    }
    
    func application(_ application: UIApplication,
                     didFailToRegisterForRemoteNotificationsWithError
                     error: Error) {
        log.error("APNs registration failed: \(error.localizedDescription)")
    }
}

/// The current APNs device token, hex-encoded; nil until registration succeeds (or on failure).
/// Not persisted: iOS delivers it again on every launch, and it can change between launches.
@MainActor
enum PushTokenStore {
    static var deviceToken: String?
}
