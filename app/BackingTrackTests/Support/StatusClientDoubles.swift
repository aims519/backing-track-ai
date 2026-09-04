import Foundation
@testable import BackingTrack

/// A `BackingTrackAPIClient` that answers successive `jobStatus` calls from a fixed script,
/// returning each entry in turn and holding the last once the script runs out (so an
/// "always pending" job is just `[.pending]`).
///
/// Shared by `GenerationProgressViewModelTests`, `JobReconcilerTests` and `HistoryViewModelTests`
/// — the poll loop and the reconcile pass are the two callers of "poll a job, decide what it means".
actor ScriptedStatusClient: BackingTrackAPIClient {
    private let script: [Result<GenerationJob, APIClientError>]
    /// Optional per-call delay, so a test can cancel a poll while it's in flight.
    private let delay: Duration
    private(set) var jobStatusCalls = 0

    init(_ script: [Result<GenerationJob, APIClientError>], delay: Duration = .zero) {
        precondition(!script.isEmpty, "script needs at least one entry")
        self.script = script
        self.delay = delay
    }

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        fatalError("these tests never submit")
    }

    func jobStatus(id: UUID) async throws -> GenerationJob {
        if delay > .zero { try? await Task.sleep(for: delay) }
        jobStatusCalls += 1
        return try script[min(jobStatusCalls - 1, script.count - 1)].get()
    }

    /// Script built from a sequence of statuses on one job id.
    static func statuses(_ statuses: [JobStatus], id: UUID) -> ScriptedStatusClient {
        ScriptedStatusClient(statuses.map { .success(.preview(id: id, status: $0)) })
    }
}
