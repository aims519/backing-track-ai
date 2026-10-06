import Foundation
import Observation
import UserNotifications

/// Hands job-completion push events from `AppDelegate` (UIKit's notification callbacks) to the
/// SwiftUI views that act on them. Owned by the delegate and injected into the environment, so
/// there's one instance without a singleton.
@MainActor
@Observable
final class NotificationRouter {
    /// Set when the user taps a job notification; `HomeView` navigates to that job's
    /// history detail and clears it. Held here rather than acted on in the delegate because a
    /// cold-launch tap arrives before any view exists to navigate.
    var pendingJobID: UUID?

    /// The job whose progress screen is currently on screen, if any. That screen already shows
    /// the result, so a foreground push for the same job is suppressed instead of repeated.
    var watchedJobID: UUID?

    func presentationOptions(forJobID jobID: UUID?) -> UNNotificationPresentationOptions {
        if let jobID, jobID == watchedJobID { return [] }
        return [.banner, .list, .sound]
    }

    /// The API's payload carries the job as a top-level `jobId` string alongside `aps`
    /// (`ApnsPayload` in the API). `nil` for anything else, e.g. a hand-sent test push.
    nonisolated static func jobID(from userInfo: [AnyHashable: Any]) -> UUID? {
        (userInfo["jobId"] as? String).flatMap(UUID.init(uuidString:))
    }
}
