import Foundation
import Testing
@testable import BackingTrack

/// A `BackingTrackAPIClient` that answers successive `jobStatus` calls from a fixed script,
/// returning each entry in turn and holding the last once the script runs out (so an
/// "always pending" job is just `[.pending]`). An `actor` because `GenerationProgressModel` calls
/// it off the main actor — same reason as `CallLog` in `GenerateTrackModelTests`.
private actor ScriptedStatusClient: BackingTrackAPIClient {
    private let script: [Result<GenerationJob, APIClientError>]
    private(set) var jobStatusCalls = 0

    init(_ script: [Result<GenerationJob, APIClientError>]) {
        precondition(!script.isEmpty, "script needs at least one entry")
        self.script = script
    }

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        fatalError("the progress screen never submits")
    }

    func jobStatus(id: UUID) async throws -> GenerationJob {
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
        config: PollingConfiguration? = nil
    ) -> GenerationProgressModel {
        GenerationProgressModel(
            job: .preview(id: id, status: startStatus),
            styleSummary: "jazz",
            apiClient: client,
            config: config ?? fastConfig
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
            config: fastConfig
        )
        #expect(unknown.progressCaption == "Working…")
    }
}
