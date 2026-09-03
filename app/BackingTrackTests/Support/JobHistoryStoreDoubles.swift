import Foundation
@testable import BackingTrack

/// A `JobHistoryStore` that records every write instead of persisting it, so the model tests can
/// assert *that* the poll loop / submit flow writes through without standing up a SwiftData stack.
/// (`SwiftDataJobHistoryStore` itself is covered in `JobHistoryStoreTests`.)
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
