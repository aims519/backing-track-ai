import Foundation
import SwiftData
import Testing
@testable import BackingTrack

/// `JobReconciler` against a real in-memory SwiftData store — `init()` seeds one `.inFlight` row
/// and hands back a reconciler wired to a scripted client the test supplies per case.
///
/// `.serialized` for the same reason as `JobHistoryStoreTests`: parallel `ModelContainer`s for one
/// `@Model` trip SwiftData's global cache.
@MainActor
@Suite(.serialized)
struct JobReconcilerTests {

    private let store: SwiftDataJobHistoryStore
    private let jobID = UUID()

    init() throws {
        let container = try ModelContainer(
            for: JobHistoryEntry.self,
            configurations: ModelConfiguration(isStoredInMemoryOnly: true)
        )
        store = SwiftDataJobHistoryStore(container: container)
    }

    /// Seeds a row at `disposition` (default `.inFlight`) and returns it.
    private func seedRow(disposition: JobDisposition = .inFlight) throws -> JobHistoryEntry {
        let submitted = GenerationJob.preview(id: jobID, status: .pending)
        try store.upsertOnSubmit(job: submitted, style: "Blues")
        if disposition != .inFlight {
            try store.recordDisposition(jobID: jobID, disposition, job: nil, error: nil)
        }
        return try #require(try store.entry(jobID: jobID))
    }

    private func reconciler(_ client: ScriptedStatusClient) -> JobReconciler {
        JobReconciler(apiClient: client, historyStore: store, notFoundRetryDelay: .milliseconds(1))
    }

    @Test func terminalSuccessAdvancesToReady() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient.statuses([.complete], id: jobID)

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .advanced(.ready))
        let row = try #require(try store.entry(jobID: jobID))
        #expect(row.disposition == .ready)
        #expect(row.status == .complete)
        #expect(row.trackID != nil)
        #expect(row.finishedAt != nil)
    }

    @Test func completedViaFallbackAlsoAdvancesToReady() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient.statuses([.completedViaFallback], id: jobID)

        #expect(await reconciler(client).reconcile(entry: entry) == .advanced(.ready))
    }

    @Test func failedStatusAdvancesToServerFailed() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient.statuses([.failed], id: jobID)

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .advanced(.serverFailed))
        #expect(try store.entry(jobID: jobID)?.disposition == .serverFailed)
    }

    /// A job cancelled from the progress screen, then later reconciled from the history screen,
    /// must land as `.cancelled` — not `.serverFailed`, which `isSuccess` alone would suggest.
    @Test func cancelledStatusAdvancesToCancelled() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient.statuses([.cancelled], id: jobID)

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .advanced(.cancelled))
        #expect(try store.entry(jobID: jobID)?.disposition == .cancelled)
    }

    @Test func stillRunningLeavesDispositionButRefreshesStatus() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient.statuses([.inProgress], id: jobID)

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .unchanged)
        let row = try #require(try store.entry(jobID: jobID))
        #expect(row.disposition == .inFlight)
        #expect(row.status == .inProgress)
    }

    @Test func singleNotFoundIsToleratedThenResolves() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient([
            .failure(.server(status: 404)),
            .success(.preview(id: jobID, status: .inProgress)),
        ])

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .unchanged)
        #expect(try store.entry(jobID: jobID)?.disposition == .inFlight)
        #expect(await client.jobStatusCalls == 2)
    }

    @Test func sustainedNotFoundIsLost() async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient([
            .failure(.server(status: 404)),
            .failure(.server(status: 404)),
        ])

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .lost)
        let row = try #require(try store.entry(jobID: jobID))
        #expect(row.disposition == .lost)
        #expect(row.lastClientError != nil)
    }

    /// An API restart empties the in-memory job store, but the track file is still on disk — a
    /// finished row must not be demoted to `.lost` by the resulting 404 (#126).
    @Test func sustainedNotFoundLeavesAReadyRowReady() async throws {
        let entry = try seedRow(disposition: .ready)
        let client = ScriptedStatusClient([
            .failure(.server(status: 404)),
            .failure(.server(status: 404)),
        ])

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .unchanged)
        let row = try #require(try store.entry(jobID: jobID))
        #expect(row.disposition == .ready)
        #expect(row.lastClientError == nil)
    }

    /// Same guard keyed on the captured `trackId`, for a row that hasn't reached `.ready` yet.
    @Test func sustainedNotFoundKeepsARowThatAlreadyHasATrack() async throws {
        let entry = try seedRow()
        entry.trackIDString = UUID().uuidString
        let client = ScriptedStatusClient([
            .failure(.server(status: 404)),
            .failure(.server(status: 404)),
        ])

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .unchanged)
        #expect(try store.entry(jobID: jobID)?.disposition == .inFlight)
    }

    @Test(arguments: [
        APIClientError.transport(URLError(.notConnectedToInternet)),
        .server(status: 503),
        .rateLimited(retryAfter: nil),
        .unauthorized,
        .missingAPIKey,
    ])
    func networkAndAuthErrorsLeaveTheRowUntouched(error: APIClientError) async throws {
        let entry = try seedRow()
        let client = ScriptedStatusClient([.failure(error)])

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .unreachable(error))
        let row = try #require(try store.entry(jobID: jobID))
        #expect(row.disposition == .inFlight)
        #expect(row.status == .pending) // unchanged from the seed
    }

    @Test func aTimedOutRowIsReconciledLikeAnInFlightOne() async throws {
        let entry = try seedRow(disposition: .timedOut)
        let client = ScriptedStatusClient.statuses([.complete], id: jobID)

        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .advanced(.ready))
        #expect(try store.entry(jobID: jobID)?.disposition == .ready)
    }

    @Test func anAlreadyReadyRowStaysReadyWhenPolledAgain() async throws {
        let entry = try seedRow(disposition: .ready)
        let client = ScriptedStatusClient.statuses([.complete], id: jobID)

        #expect(await reconciler(client).reconcile(entry: entry) == .advanced(.ready))
        #expect(try store.entry(jobID: jobID)?.disposition == .ready)
    }

    @Test func aRowWithNoJobIDIsNotInHistory() async throws {
        let entry = try seedRow()
        entry.jobIDString = "not-a-uuid"

        let client = ScriptedStatusClient.statuses([.complete], id: jobID)
        let outcome = await reconciler(client).reconcile(entry: entry)

        #expect(outcome == .notInHistory)
        #expect(await client.jobStatusCalls == 0)
    }
}
