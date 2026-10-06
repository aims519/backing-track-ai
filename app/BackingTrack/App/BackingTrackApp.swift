import SwiftData
import SwiftUI

@main
struct BackingTrackApp: App {
    @UIApplicationDelegateAdaptor private var appDelegate: AppDelegate
    
    var body: some Scene {
        WindowGroup {
            HomeView()
                .environment(appDelegate.notificationRouter)
        }
        // `.modelContainer(_:)` (not `.modelContainer(for:)`, which would build a *second*
        // container) puts the same store `JobHistoryStore` writes into the environment, so
        // `HistoryView`'s `@Query` and the reconcile/delete writes share one `mainContext`.
        .modelContainer(JobHistoryContainer.shared)
    }
}
