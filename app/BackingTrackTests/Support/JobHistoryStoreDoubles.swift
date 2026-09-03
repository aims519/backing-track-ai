import Foundation
@testable import BackingTrack

/// A `JobHistoryStore` that records every write instead of persisting it — lets the model tests
/// assert *that* the poll loop / submit flow writes through, without standing up a SwiftData
/// stack. (`SwiftDataJobHistoryStore` itself is covered directly in `JobHistoryStoreTests`.)
///
/// `@MainActor` to match the protocol; the model tests are already main-actor.
@MainActor
final class RecordingJobHistoryStore: JobHistoryStore {
    private(set) var upserts: [(job: GenerationJob, style: String)] = []
    private(set) var statuses: [(jobID: UUID, status: JobStatus, startedAt: Date?)] = []
    private(set) var dispositions: [(jobID: UUID, disposition: JobDisposition, job: GenerationJob?, error: String?)] = []

    /// When set, every method throws it — for checking that a store failure never derails a
    /// generation (ADR 0024's headline invariant). Calls are still recorded first, so a test can
    /// assert the write was *attempted*.
    var errorToThrow: (any Error)?

    struct StoreFailure: Error {}

    /// The last disposition written — the usual thing a progress-model test wants to check.
    var lastDisposition: JobDisposition? { dispositions.last?.disposition }

    func upsertOnSubmit(job: GenerationJob, style: String) throws {
        upserts.append((job, style))
        if let errorToThrow { throw errorToThrow }
    }

    func recordStatus(jobID: UUID, status: JobStatus, startedAt: Date?) throws {
        statuses.append((jobID, status, startedAt))
        if let errorToThrow { throw errorToThrow }
    }

    func recordDisposition(jobID: UUID, _ disposition: JobDisposition, job: GenerationJob?, error: String?) throws {
        dispositions.append((jobID, disposition, job, error))
        if let errorToThrow { throw errorToThrow }
    }

    func entry(jobID: UUID) throws -> JobHistoryEntry? {
        if let errorToThrow { throw errorToThrow }
        return nil
    }
    func allEntries() throws -> [JobHistoryEntry] {
        if let errorToThrow { throw errorToThrow }
        return []
    }
}
