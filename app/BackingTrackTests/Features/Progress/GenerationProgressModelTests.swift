import Foundation
import Testing
@testable import BackingTrack

/// A `BackingTrackAPIClient` that answers successive `jobStatus` calls from a fixed script,
/// returning each entry in turn and holding the last once the script runs out (so an
/// "always pending" job is just `[.pending]`). An `actor` because `GenerationProgressModel` calls
/// it off the main actor — same reason as `CallLog` in `GenerateTrackModelTests`.
private actor ScriptedStatusClient: BackingTrackAPIClient {
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
        fatalError("the progress screen never submits")
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

private extension GenerationProgressModel.Phase {
    var readyJob: GenerationJob? {
        if case .ready(let job) = self { return job } else { return nil }
    }
}

// `@MainActor` because `GenerationProgressModel` is main-actor isolated — the tests drive it
// exactly as the view's `.task` would.
@MainActor
struct GenerationProgressModelTests {

    /// Millisecond durations so the poll loop runs to completion near-instantly.
    private let fastConfig = PollingConfiguration(
        interval: .milliseconds(1),
        timeout: .milliseconds(50),
        maxConsecutiveFailures: 3
    )

    private func makeModel(
        id: UUID,
        client: ScriptedStatusClient,
        startStatus: JobStatus = .pending,
        config: PollingConfiguration? = nil,
        historyStore: JobHistoryStore = RecordingJobHistoryStore()
    ) -> GenerationProgressModel {
        GenerationProgressModel(
            job: .preview(id: id, status: startStatus),
            styleSummary: "jazz",
            apiClient: client,
            config: config ?? fastConfig,
            historyStore: historyStore
        )
    }

    @Test func pollsUntilComplete() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.pending, .inProgress, .complete], id: id)
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase.readyJob?.status == .complete)
        #expect(model.latestStatus == .complete)
        #expect(await client.jobStatusCalls == 3)
    }

    @Test func completedViaFallbackIsReady() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.inProgress, .completedViaFallback], id: id)
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase.readyJob?.status == .completedViaFallback)
    }

    @Test func serverFailedStatusEndsInFailed() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.inProgress, .failed], id: id)
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase == .failed(.jobFailed))
    }

    @Test func alreadyTerminalJobResolvesWithoutPolling() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.complete], id: id)
        let model = makeModel(id: id, client: client, startStatus: .complete)

        #expect(model.phase.readyJob?.status == .complete) // resolved in init

        await model.startPolling()

        #expect(await client.jobStatusCalls == 0)
    }

    @Test func toleratesTransientErrors() async {
        let id = UUID()
        let client = ScriptedStatusClient([
            .failure(.transport(URLError(.timedOut))),
            .success(.preview(id: id, status: .inProgress)),
            .success(.preview(id: id, status: .complete)),
        ])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase.readyJob != nil)
    }

    @Test func givesUpAfterConsecutiveErrors() async {
        let id = UUID()
        let client = ScriptedStatusClient([.failure(.server(status: 503))])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase == .failed(.pollError(.server(status: 503))))
        #expect(await client.jobStatusCalls == 3) // maxConsecutiveFailures
    }

    @Test func unauthorizedFailsImmediately() async {
        let id = UUID()
        let client = ScriptedStatusClient([.failure(.unauthorized)])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase == .failed(.pollError(.unauthorized)))
        #expect(await client.jobStatusCalls == 1)
    }

    @Test func notFoundBlipIsToleratedOnce() async {
        let id = UUID()
        let client = ScriptedStatusClient([
            .success(.preview(id: id, status: .pending)),
            .failure(.server(status: 404)),
            .success(.preview(id: id, status: .inProgress)),
            .success(.preview(id: id, status: .complete)),
        ])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase.readyJob != nil)
    }

    @Test func sustainedNotFoundMeansLost() async {
        let id = UUID()
        let client = ScriptedStatusClient([
            .success(.preview(id: id, status: .pending)),
            .failure(.server(status: 404)),
            .failure(.server(status: 404)),
        ])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase == .failed(.lost))
    }

    @Test func unexpectedRateLimitIsTransient() async {
        let id = UUID()
        let client = ScriptedStatusClient([
            .failure(.rateLimited(retryAfter: nil)),
            .success(.preview(id: id, status: .complete)),
        ])
        let model = makeModel(id: id, client: client)

        await model.startPolling()

        #expect(model.phase.readyJob != nil)
    }

    @Test func timesOutWhenJobNeverFinishes() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.pending], id: id)
        let model = makeModel(
            id: id,
            client: client,
            config: PollingConfiguration(
                interval: .milliseconds(5),
                timeout: .milliseconds(30),
                maxConsecutiveFailures: 3
            )
        )

        await model.startPolling()

        #expect(model.phase == .timedOut)
    }

    @Test func cancellationLeavesPhaseUnresolved() async {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.pending], id: id)
        let model = makeModel(id: id, client: client)

        let task = Task { await model.startPolling() }
        try? await Task.sleep(for: .milliseconds(10)) // let a few polls happen
        task.cancel()
        await task.value

        #expect(model.phase == .polling)
        #expect(!model.phase.isTerminal)
    }

    /// A poll cancelled mid-flight comes back as `APIClientError.transport(URLError(.cancelled))`,
    /// not `CancellationError` — it must not be counted as a failed poll. `maxConsecutiveFailures`
    /// of 1 means that without the `Task.isCancelled` guard this would land in `.failed`.
    @Test func cancelledMidPollIsNotCountedAsAFailure() async {
        let id = UUID()
        let client = ScriptedStatusClient(
            [.failure(.transport(URLError(.cancelled)))],
            delay: .milliseconds(50)
        )
        let model = makeModel(
            id: id,
            client: client,
            config: PollingConfiguration(
                interval: .milliseconds(1),
                timeout: .seconds(10),
                maxConsecutiveFailures: 1
            )
        )

        let task = Task { await model.startPolling() }
        try? await Task.sleep(for: .milliseconds(5)) // let the poll get in flight
        task.cancel()
        await task.value

        #expect(model.phase == .polling)
    }

    @Test func progressCaptionMapsStatus() {
        let id = UUID()
        let client = ScriptedStatusClient.statuses([.pending], id: id)

        let pending = makeModel(id: id, client: client, startStatus: .pending)
        #expect(pending.progressCaption == "Queued…")

        let inProgress = makeModel(id: id, client: client, startStatus: .inProgress)
        #expect(inProgress.progressCaption == "Composing your track…")

        // An unrecognised backend state still yields a caption rather than crashing.
        let unknown = GenerationProgressModel(
            job: .preview(id: id, status: .unknown("Reticulating")),
            styleSummary: nil,
            apiClient: client,
            config: fastConfig,
            historyStore: RecordingJobHistoryStore()
        )
        #expect(unknown.progressCaption == "Working…")
    }

    // MARK: - History write-through
    //
    // The store is a durable mirror of the poll loop (ADR 0023: a job the screen stops following
    // must still be findable later). These check the disposition that lands for each outcome.

    @Test func mirrorsInProgressStatusWhilePolling() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.pending, .inProgress, .complete], id: id)
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        #expect(store.statuses.contains { $0.status == .inProgress })
        #expect(store.lastDisposition == .ready)
        #expect(store.dispositions.last?.job?.trackId != nil)
    }

    @Test func writesServerFailedDisposition() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.inProgress, .failed], id: id)
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        #expect(store.lastDisposition == .serverFailed)
    }

    @Test func writesLostDisposition() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient([
            .success(.preview(id: id, status: .pending)),
            .failure(.server(status: 404)),
            .failure(.server(status: 404)),
        ])
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        #expect(store.lastDisposition == .lost)
    }

    @Test func writesTimedOutDisposition() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.pending], id: id)
        let model = makeModel(
            id: id,
            client: client,
            config: PollingConfiguration(
                interval: .milliseconds(5),
                timeout: .milliseconds(30),
                maxConsecutiveFailures: 3
            ),
            historyStore: store
        )

        await model.startPolling()

        #expect(store.lastDisposition == .timedOut)
    }

    @Test func writesPollErrorDispositionWithDetail() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient([.failure(.server(status: 503))])
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        #expect(store.lastDisposition == .pollError)
        #expect(store.dispositions.last?.error != nil)
    }

    @Test func cancellationWritesNoTerminalDisposition() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.pending], id: id)
        let model = makeModel(id: id, client: client, historyStore: store)

        let task = Task { await model.startPolling() }
        try? await Task.sleep(for: .milliseconds(10))
        task.cancel()
        await task.value

        // A dismissed screen leaves the job `.inFlight` — `transition` is never reached, so no
        // disposition is recorded at all (the row keeps the `.inFlight` its insert gave it).
        #expect(store.dispositions.isEmpty)
    }

    @Test func alreadyTerminalOnArrivalWritesDisposition() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.complete], id: id)

        _ = makeModel(id: id, client: client, startStatus: .complete, historyStore: store)

        #expect(store.lastDisposition == .ready)
    }

    @Test func selfHealsHistoryAtPollStart() async {
        // Even if GenerateTrackModel's insert failed, the poll loop re-inserts the job so it can't
        // be lost from history.
        let id = UUID()
        let store = RecordingJobHistoryStore()
        let client = ScriptedStatusClient.statuses([.inProgress, .complete], id: id)
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        #expect(store.upserts.contains { $0.job.id == id })
    }

    @Test func aStoreFailureDoesNotBreakPolling() async {
        let id = UUID()
        let store = RecordingJobHistoryStore()
        store.errorToThrow = RecordingJobHistoryStore.StoreFailure()
        let client = ScriptedStatusClient.statuses([.pending, .inProgress, .complete], id: id)
        let model = makeModel(id: id, client: client, historyStore: store)

        await model.startPolling()

        // Every store call threw, yet the screen still reached the track.
        #expect(model.phase.readyJob?.status == .complete)
    }
}
