import Foundation
import SwiftData
import Testing
@testable import BackingTrack

/// `HistoryViewModel`'s reconcile / delete / watch behaviour, against a real in-memory store.
///
/// `.serialized` for the usual SwiftData-`ModelContainer` reason (see `JobHistoryStoreTests`).
@MainActor
@Suite(.serialized)
struct HistoryViewModelTests {

    private let store: SwiftDataJobHistoryStore

    init() throws {
        let container = try ModelContainer(
            for: JobHistoryEntry.self,
            configurations: ModelConfiguration(isStoredInMemoryOnly: true)
        )
        store = SwiftDataJobHistoryStore(container: container)
    }

    /// Inserts a row at `disposition` and returns its job id.
    @discardableResult
    private func seed(_ disposition: JobDisposition, status: JobStatus = .pending) throws -> UUID {
        let id = UUID()
        try store.upsertOnSubmit(job: .preview(id: id, status: status), style: "Blues")
        if disposition != .inFlight {
            try store.recordDisposition(jobID: id, disposition, job: nil, error: nil)
        }
        return id
    }

    private func model(_ client: ScriptedStatusClient) -> HistoryViewModel {
        HistoryViewModel(apiClient: client, historyStore: store)
    }

    @Test func reconcileTouchesOnlyInFlightAndTimedOutRows() async throws {
        try seed(.inFlight)
        try seed(.timedOut)
        try seed(.ready)
        try seed(.serverFailed)
        try seed(.lost)
        let client = ScriptedStatusClient([.success(.preview(status: .inProgress))])

        await model(client).reconcileVisible(try store.allEntries())

        #expect(await client.jobStatusCalls == 2)
    }

    @Test func reconcileSurfacesAnUnreachableErrorAsABanner() async throws {
        try seed(.inFlight)
        let client = ScriptedStatusClient([.failure(.transport(URLError(.notConnectedToInternet)))])
        let vm = model(client)

        await vm.reconcileVisible(try store.allEntries())

        #expect(vm.refreshError != nil)
    }

    @Test func aPollCutShortByNavigationDoesNotRaiseABanner() async throws {
        // The detail view's on-open reconcile is cancelled when the screen is popped mid-poll;
        // that surfaces as a cancelled transport error and must not leave a spurious banner.
        let id = try seed(.inFlight)
        let entry = try #require(try store.entry(jobID: id))
        let vm = model(ScriptedStatusClient([.failure(.transport(URLError(.cancelled)))]))

        await vm.checkAgain(entry)

        #expect(vm.refreshError == nil)
    }

    @Test func aNewPassClearsAStaleBanner() async throws {
        try seed(.inFlight)
        let vm = model(ScriptedStatusClient([.failure(.server(status: 503))]))
        await vm.reconcileVisible(try store.allEntries())
        #expect(vm.refreshError != nil)

        await vm.reconcileVisible([]) // an empty pass still clears the banner up front
        #expect(vm.refreshError == nil)
    }

    @Test func aConcurrentSecondPassIsIgnored() async throws {
        try seed(.inFlight)
        try seed(.inFlight)
        let client = ScriptedStatusClient(
            [.success(.preview(status: .inProgress))],
            delay: .milliseconds(50)
        )
        let vm = model(client)
        let entries = try store.allEntries()

        let first = Task { await vm.reconcileVisible(entries) }
        try await Task.sleep(for: .milliseconds(5))
        await vm.reconcileVisible(entries) // returns immediately — isReconciling is set
        await first.value

        #expect(await client.jobStatusCalls == 2) // the second call did nothing
    }

    @Test func cancellingThePassStopsItPartway() async throws {
        for _ in 0..<4 { try seed(.inFlight) }
        let client = ScriptedStatusClient(
            [.success(.preview(status: .inProgress))],
            delay: .milliseconds(30)
        )
        let vm = model(client)
        let entries = try store.allEntries()

        let task = Task { await vm.reconcileVisible(entries) }
        try await Task.sleep(for: .milliseconds(45))
        task.cancel()
        await task.value

        #expect(await client.jobStatusCalls < 4)
    }

    // MARK: - keepReconcilingInFlight

    @Test func theRefreshLoopPollsUntilTheJobFinishesThenStops() async throws {
        let id = try seed(.inFlight)
        let entry = try #require(try store.entry(jobID: id))
        let client = ScriptedStatusClient.statuses([.inProgress, .inProgress, .complete], id: id)

        await model(client).keepReconcilingInFlight([entry], every: .milliseconds(1))

        #expect(await client.jobStatusCalls == 3) // returned by itself once the row left .inFlight
        #expect(entry.disposition == .ready)
    }

    @Test func theRefreshLoopDoesNothingWhenNothingIsInFlight() async throws {
        let ready = try #require(try store.entry(jobID: try seed(.ready)))
        let timedOut = try #require(try store.entry(jobID: try seed(.timedOut)))
        let client = ScriptedStatusClient([.success(.preview(status: .inProgress))])

        // `.timedOut` is reconcile-on-open's business; looping over it would poll it forever.
        await model(client).keepReconcilingInFlight([ready, timedOut], every: .milliseconds(1))

        #expect(await client.jobStatusCalls == 0)
    }

    @Test func theRefreshLoopStopsWhenCancelled() async throws {
        let id = try seed(.inFlight)
        let entry = try #require(try store.entry(jobID: id))
        let client = ScriptedStatusClient.statuses([.inProgress], id: id)
        let vm = model(client)

        // An hour between passes: only cancellation can end this in time.
        let task = Task { await vm.keepReconcilingInFlight([entry], every: .seconds(3600)) }
        try await Task.sleep(for: .milliseconds(50))
        task.cancel()
        await task.value

        #expect(await client.jobStatusCalls == 1)
        #expect(entry.disposition == .inFlight)
    }

    @Test func theRefreshLoopLeavesTheWatchedRowToItsOwnPollLoop() async throws {
        let id = try seed(.inFlight, status: .inProgress)
        let entry = try #require(try store.entry(jobID: id))
        let client = ScriptedStatusClient.statuses([.inProgress], id: id)
        let vm = model(client)
        vm.startWatching(entry)

        // Cancelled almost at once: with the only row watched, each pass is a no-op.
        let task = Task { await vm.keepReconcilingInFlight([entry], every: .milliseconds(5)) }
        try await Task.sleep(for: .milliseconds(30))
        task.cancel()
        await task.value

        #expect(await client.jobStatusCalls == 0)
    }

    @Test func deleteRemovesTheRowThroughTheStore() async throws {
        let recording = RecordingJobHistoryStore()
        let vm = HistoryViewModel(apiClient: ScriptedStatusClient([.success(.preview())]), historyStore: recording)
        let entry = JobHistoryEntry.preview()

        vm.delete(entry)

        #expect(recording.deletions == [try #require(entry.jobID)])
    }

    @Test func deleteSwallowsAStoreFailure() async throws {
        let recording = RecordingJobHistoryStore()
        recording.errorToThrow = RecordingJobHistoryStore.StoreFailure()
        let vm = HistoryViewModel(apiClient: ScriptedStatusClient([.success(.preview())]), historyStore: recording)

        vm.delete(JobHistoryEntry.preview()) // must not throw

        #expect(recording.deletions.count == 1)
    }

    @Test func watchProgressViewModelIsMemoisedPerJob() async throws {
        let vm = model(ScriptedStatusClient([.success(.preview(status: .inProgress))]))
        let entry = JobHistoryEntry.preview(status: .inProgress, disposition: .inFlight)

        vm.startWatching(entry)
        let seed = try #require(vm.watchSeed)
        let a = vm.progressViewModel(for: seed)
        let b = vm.progressViewModel(for: seed)

        #expect(a === b)
    }

    @Test func reconcileSkipsTheRowBeingWatched() async throws {
        let id = try seed(.inFlight, status: .inProgress)
        let entry = try #require(try store.entry(jobID: id))
        let client = ScriptedStatusClient([.success(.preview(id: id, status: .complete))])
        let vm = model(client)

        vm.startWatching(entry)
        await vm.reconcileVisible(try store.allEntries())

        #expect(await client.jobStatusCalls == 0)
    }
}
