import Foundation
import SwiftData

/// The write path for local job/track history. A protocol so the `@Observable` models can be
/// tested without a real SwiftData stack — the role `APIKeyStoring` plays for the Keychain.
///
/// `@MainActor`, and *not* `Sendable`: unlike `APIKeyStoring` / `BackingTrackAPIClient` this never
/// crosses an actor boundary. Both callers are already `@MainActor` and stay on the main actor
/// across their `await`s, so the store, its `ModelContext` and the `@Model` rows it returns never
/// need to be `Sendable`. A future background/bulk write would be a `ModelActor` behind this same
/// protocol.
///
/// Ids are passed as `UUID` (callers hold `GenerationJob.id`); the implementation converts to the
/// stored `uuidString` form.
@MainActor
protocol JobHistoryStore {
    /// Records a job the moment the API accepts it, and again when the progress screen opens
    /// (self-healing a failed first write). Idempotent: for an existing row it refreshes the
    /// request parameters, keeps `createdAt`, and never regresses a status the poll loop has
    /// already advanced.
    func upsertOnSubmit(job: GenerationJob, style: String) throws

    /// Mirrors a non-terminal status seen while polling. No-op if the row is gone.
    func recordStatus(jobID: UUID, status: JobStatus, startedAt: Date?) throws

    /// Records the client's terminal disposition (`ready` / `serverFailed` / `timedOut` / `lost` /
    /// `pollError`). When `job` is given, also folds in its `status`, `startTime`, `endTime` and
    /// `trackId`. No-op if the row is gone.
    func recordDisposition(jobID: UUID, _ disposition: JobDisposition, job: GenerationJob?, error: String?) throws

    func entry(jobID: UUID) throws -> JobHistoryEntry?

    /// Newest first. For the future history screen's non-reactive callers; the screen itself
    /// should prefer `@Query`.
    func allEntries() throws -> [JobHistoryEntry]
}

/// SwiftData-backed `JobHistoryStore`, writing through the container's main-actor context.
@MainActor
struct SwiftDataJobHistoryStore: JobHistoryStore {
    /// `nil` means "the shared on-disk store", resolved on first use. Deferring it this way keeps
    /// merely *constructing* the store — which every model does as a default init argument, most
    /// of them only to satisfy the compiler in a form-logic test — from opening SwiftData.
    private let explicitContainer: ModelContainer?

    init(container: ModelContainer? = nil) {
        self.explicitContainer = container
    }

    private var context: ModelContext {
        (explicitContainer ?? JobHistoryContainer.shared).mainContext
    }

    func upsertOnSubmit(job: GenerationJob, style: String) throws {
        if let existing = try entry(jobID: job.id) {
            // Refresh the request parameters (they can only have been the same anyway) but leave
            // the lifecycle fields alone — a submit response is always Pending/InProgress, so
            // writing its status back would regress a row the poll loop has already finished.
            existing.mode = job.mode
            existing.tempo = job.tempo
            existing.chordSymbols = job.chords
            existing.keyRoot = job.keyRoot
            existing.timeSignatureBeatCount = job.timeSignatureBeatCount
            existing.timeSignatureBeatUnit = job.timeSignatureBeatUnit
            existing.style = style
            if !existing.status.isTerminal { existing.status = job.status }
            if existing.startedAt == nil { existing.startedAt = job.startTime }
            if existing.trackIDString == nil { existing.trackIDString = job.trackId?.uuidString }
        } else {
            context.insert(JobHistoryEntry(
                jobIDString: job.id.uuidString,
                statusWireValue: job.status.wireValue,
                dispositionRawValue: JobDisposition.inFlight.rawValue,
                mode: job.mode,
                tempo: job.tempo,
                chordSymbols: job.chords,
                keyRoot: job.keyRoot,
                timeSignatureBeatCount: job.timeSignatureBeatCount,
                timeSignatureBeatUnit: job.timeSignatureBeatUnit,
                style: style,
                startedAt: job.startTime,
                trackIDString: job.trackId?.uuidString
            ))
        }
        try context.save()
    }

    func recordStatus(jobID: UUID, status: JobStatus, startedAt: Date?) throws {
        guard let entry = try entry(jobID: jobID) else { return }
        entry.status = status
        // The server sets `startTime` once and never changes it — first write wins.
        if entry.startedAt == nil, let startedAt { entry.startedAt = startedAt }
        try context.save()
    }

    func recordDisposition(jobID: UUID, _ disposition: JobDisposition, job: GenerationJob?, error: String?) throws {
        guard let entry = try entry(jobID: jobID) else { return }
        entry.disposition = disposition
        entry.lastClientError = error
        if let job {
            entry.status = job.status
            if entry.startedAt == nil { entry.startedAt = job.startTime }
            if let endTime = job.endTime { entry.finishedAt = endTime }
            if let trackId = job.trackId { entry.trackIDString = trackId.uuidString }
        }
        try context.save()
    }

    func entry(jobID: UUID) throws -> JobHistoryEntry? {
        // Fetched by scanning rather than a `#Predicate` on `jobIDString`: the history is a
        // single user's own generations (tens of rows, not thousands), and `#Predicate`
        // string-equality against an in-memory store has been crash-prone. Revisit with an
        // indexed predicate if the row count ever justifies it.
        let key = jobID.uuidString
        return try context.fetch(FetchDescriptor<JobHistoryEntry>())
            .first { $0.jobIDString == key }
    }

    func allEntries() throws -> [JobHistoryEntry] {
        try context.fetch(FetchDescriptor<JobHistoryEntry>(
            sortBy: [SortDescriptor(\.createdAt, order: .reverse)]
        ))
    }
}

#if DEBUG
/// A `JobHistoryStore` that drops every write — for `#Preview`s, which shouldn't touch the real
/// on-disk store. (Tests use their own fakes in the test target.)
@MainActor
struct PreviewJobHistoryStore: JobHistoryStore {
    func upsertOnSubmit(job: GenerationJob, style: String) throws {}
    func recordStatus(jobID: UUID, status: JobStatus, startedAt: Date?) throws {}
    func recordDisposition(jobID: UUID, _ disposition: JobDisposition, job: GenerationJob?, error: String?) throws {}
    func entry(jobID: UUID) throws -> JobHistoryEntry? { nil }
    func allEntries() throws -> [JobHistoryEntry] { [] }
}
#endif
