import Foundation
import SwiftData

/// The app's single SwiftData store, holding `JobHistoryEntry` rows.
///
/// A lazy `static let` rather than an `@Environment`-injected container: the write path runs from
/// `@MainActor @Observable` models (`GenerateTrackViewModel`, `GenerationProgressViewModel`), not just
/// views, so it needs a handle that doesn't depend on being inside a SwiftUI `body`.
///
/// `BackingTrackApp` also hands this same instance to `.modelContainer(_:)` so a future history
/// screen can use `@Query` — reads go through `@Query`, writes go through `JobHistoryStore`.
enum JobHistoryContainer {
    static let shared: ModelContainer = make()

    private static func make() -> ModelContainer {
        let schema = Schema([JobHistoryEntry.self])
        let onDisk = ModelConfiguration(schema: schema)
        // `Application Support` doesn't exist on a fresh install. Without this, SwiftData's first
        // open fails with an `[error]`-level `NSCocoaError 512` and a large filesystem dump before
        // CoreData's internal recovery quietly creates the directory and retries — every first
        // launch and every reinstall.
        try? FileManager.default.createDirectory(
            at: onDisk.url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        do {
            return try ModelContainer(for: schema, configurations: onDisk)
        } catch {
            // Most likely an unmigratable store from an older schema (there's no `SchemaMigrationPlan`
            // yet — see ADR 0024). History has no retention guarantee, so wiping and rebuilding
            // beats bricking the app. Try once on a clean slate...
            removeStore(at: onDisk.url)
            if let rebuilt = try? ModelContainer(for: schema, configurations: onDisk) {
                return rebuilt
            }
            // ...and if even that fails (disk denied, sandbox issue), fall back to an in-memory
            // store: history won't survive this launch, but generation still works. Never crash
            // the app over persistence.
            let inMemory = ModelConfiguration(schema: schema, isStoredInMemoryOnly: true)
            return try! ModelContainer(for: schema, configurations: inMemory)
        }
    }

    /// Deletes the SQLite store and its sidecar files.
    private static func removeStore(at url: URL) {
        let manager = FileManager.default
        for suffix in ["", "-wal", "-shm"] {
            let sidecar = url.deletingLastPathComponent()
                .appendingPathComponent(url.lastPathComponent + suffix)
            try? manager.removeItem(at: sidecar)
        }
    }
}
