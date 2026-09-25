import os
import UIKit
import UserNotifications

final class AppDelegate: NSObject, UIApplicationDelegate {
    private let log = Logger(subsystem: "com.andrewsavory.backingtrack", category: "push")

    let notificationRouter = NotificationRouter()

    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions:[UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // Must be set before launch finishes, or a tap that cold-launches the app is never
        // delivered to didReceive.
        UNUserNotificationCenter.current().delegate = self

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

// MARK: - UNUserNotificationCenterDelegate

// `@preconcurrency` so these can stay main-actor methods: the protocol's requirements are
// nonisolated and take non-Sendable UNNotification objects, which Swift 6 otherwise rejects.
// The alternative, `nonisolated async` methods, compiles but crashes on tap: the async thunk
// completes iOS's hidden completion handler off the main thread, and UIKit's snapshot update
// behind it asserts. UserNotifications calls these on the main thread, which @preconcurrency
// checks at runtime.
extension AppDelegate: @preconcurrency UNUserNotificationCenterDelegate {
    // Without this, iOS drops a push that arrives while the app is in the foreground.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        let jobID = NotificationRouter.jobID(from: notification.request.content.userInfo)
        log.info("Push received in foreground for job \(jobID?.uuidString ?? "<none>", privacy: .public)")
        completionHandler(notificationRouter.presentationOptions(forJobID: jobID))
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        defer { completionHandler() }
        guard let jobID = NotificationRouter.jobID(from: response.notification.request.content.userInfo) else {
            log.error("Tapped push had no readable jobId; nothing to open")
            return
        }
        log.info("Push tapped for job \(jobID.uuidString, privacy: .public)")
        notificationRouter.pendingJobID = jobID
    }
}

/// The current APNs device token, hex-encoded; nil until registration succeeds (or on failure).
/// Not persisted: iOS delivers it again on every launch, and it can change between launches.
@MainActor
enum PushTokenStore {
    static var deviceToken: String?
}
