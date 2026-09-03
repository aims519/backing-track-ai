import SwiftData
import SwiftUI

@main
struct BackingTrackApp: App {
    var body: some Scene {
        WindowGroup {
            GenerateTrackView()
        }
        // `.modelContainer(_:)` (not `.modelContainer(for:)`, which would build a *second*
        // container) puts the same store `JobHistoryStore` writes into the environment. Nothing
        // reads it via `@Query` yet — it's the one hook the job-history screen will need.
        .modelContainer(JobHistoryContainer.shared)
    }
}
