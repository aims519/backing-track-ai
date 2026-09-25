import UserNotifications

enum NotificationPermission {
    /// Shows the system prompt only the first time; afterwards iOS returns the stored answer
    /// without prompting, so this is safe to call on every submit.
    static func request() async {
        // The answer is deliberately ignored: the device token is sent either way, and a user who
        // declines simply never sees the push — APNs still accepts it and iOS drops it silently.
        _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
    }
}
