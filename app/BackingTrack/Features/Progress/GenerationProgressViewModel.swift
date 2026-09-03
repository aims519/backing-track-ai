import Foundation
import Observation

/// Backing state for `GenerationProgressView` — polls `GET /tracks/status/{jobId}` for an accepted
/// generation job and drives the screen through polling → ready / failed / timed-out.
///
/// `@MainActor @Observable` for the same reasons as `GenerateTrackViewModel`: every mutation is driven
/// from (and read back by) the UI on the main thread, and it satisfies Swift 6 strict concurrency
/// without scattering `await` through the view. The poll loop is one `async` method that the view
/// runs from `.task`, so cancellation is automatic when the screen goes away — the model holds no
/// `Task` of its own.
@MainActor
@Observable
final class GenerationProgressViewModel {

    /// Where the screen is in following the job. `.polling` is the only non-terminal case.
    enum Phase: Equatable {
        case polling
        /// The job finished with a usable track (`Complete` or `CompletedViaFallback`).
        case ready(GenerationJob)
        case failed(Failure)
        /// Polling ran past `PollingConfiguration.timeout` without the job reaching a terminal
        /// state. The job may still finish server-side — there's just nowhere to see it yet.
        case timedOut

        var isTerminal: Bool {
            if case .polling = self { return false }
            return true
        }
    }

    /// Why the screen stopped without a usable track.
    enum Failure: Equatable {
        /// The server reported the job as `Failed`.
        case jobFailed
        /// The server returned 404 for the job id it had just issued — the in-memory job store
        /// lost it, typically an API restart (see ADR 0013).
        case lost
        /// Gave up after `PollingConfiguration.maxConsecutiveFailures` consecutive poll errors,
        /// or hit an error that won't self-heal (a rejected key, say).
        case pollError(APIClientError)
    }

    @ObservationIgnored private let apiClient: BackingTrackAPIClient
    @ObservationIgnored private let config: PollingConfiguration
    @ObservationIgnored private let jobID: UUID
    /// Durable job/track history. Every `phase` change is mirrored here (via `transition(to:)`),
    /// so a job the screen stops following — timed out, or lost to an API restart — still has a
    /// row the user can find later (ADR 0023). A write failure is swallowed; see `recordToHistory`.
    @ObservationIgnored private let historyStore: JobHistoryStore
    /// When this screen opened — the fallback origin for the elapsed-time display if the server
    /// didn't send a `startTime`.
    @ObservationIgnored private let openedAt = Date()
    /// Text of the last poll error, stored on the history row when the disposition is `.pollError`.
    @ObservationIgnored private var lastClientErrorText: String?

    /// The job as first accepted — the source for the request-summary card and the elapsed-time
    /// origin. Not replaced as polling proceeds; `latestStatus` carries the live state.
    let initialJob: GenerationJob
    /// The composed style string. Passed in separately because `GenerationJob` doesn't echo it
    /// back (`GenerationJobDto` omits `Style`).
    let styleSummary: String?

    private(set) var phase: Phase = .polling
    /// The most recent status seen from the server — drives `progressCaption` while `.polling`.
    private(set) var latestStatus: JobStatus
    /// Successful status polls so far. Handy for tests and a debug overlay.
    private(set) var pollCount = 0

    init(
        job: GenerationJob,
        styleSummary: String?,
        apiClient: BackingTrackAPIClient,
        config: PollingConfiguration = .live,
        historyStore: JobHistoryStore = SwiftDataJobHistoryStore()
    ) {
        self.jobID = job.id
        self.initialJob = job
        self.styleSummary = styleSummary
        self.apiClient = apiClient
        self.config = config
        self.historyStore = historyStore
        self.latestStatus = job.status
        // A job that's somehow already terminal on arrival (e.g. an instant failure) shouldn't
        // start the loop at all.
        if job.status.isTerminal {
            transition(to: job.status.isSuccess ? .ready(job) : .failed(.jobFailed), job: job)
        }
    }

    // MARK: - Derived copy

    /// User-facing progress line, derived on the client from the five `JobStates` — ADR 0013's
    /// status model carries no stage message. Backend-supplied stage text ("applying repairs" and
    /// so on) is a separate future task; see ADR 0023.
    var progressCaption: String {
        switch latestStatus {
        case .pending:
            "Queued…"
        case .inProgress:
            "Composing your track…"
        case .complete, .completedViaFallback, .failed:
            // Reached only in the brief window between seeing a terminal status and `phase`
            // switching away from `.polling`.
            "Finishing up…"
        case .unknown:
            "Working…"
        }
    }

    /// The instant elapsed time is measured from: the server's job start if it gave one, else
    /// when this screen opened.
    var elapsedOrigin: Date { initialJob.startTime ?? openedAt }

    // MARK: - Polling

    /// Polls the status endpoint until the job reaches a terminal state, the timeout is hit, or
    /// the task is cancelled (the screen was dismissed). Safe to call once per screen; a second
    /// call is a no-op once `phase` has left `.polling`.
    func startPolling() async {
        guard case .polling = phase else { return }

        // Make sure this job has a history row before the loop can write dispositions to it.
        // Normally `GenerateTrackViewModel` inserted it at submit time; doing it here too (idempotent)
        // means a failed insert there still self-heals, so a job can never be lost from history.
        recordToHistory {
            try historyStore.upsertOnSubmit(job: initialJob, style: styleSummary ?? "")
        }

        // Monotonic — a wall-clock change mustn't move the deadline. The *displayed* elapsed time
        // uses `Date` because it's shown relative to the server's `startTime`.
        let deadline = ContinuousClock.now + config.timeout
        var consecutiveFailures = 0
        var hasRecordedStartTime = initialJob.startTime != nil

        while !Task.isCancelled {
            do {
                let job = try await apiClient.jobStatus(id: jobID)
                consecutiveFailures = 0
                pollCount += 1
                let statusChanged = job.status != latestStatus
                latestStatus = job.status

                if job.status.isTerminal {
                    transition(to: job.status.isSuccess ? .ready(job) : .failed(.jobFailed), job: job)
                    return
                }
                // Touch the store on an actual transition (Pending → InProgress), or the one poll
                // where a `startTime` first appears — but not on every identical tick, since polls
                // land every 1-2s.
                let startTimeArrived = job.startTime != nil && !hasRecordedStartTime
                if statusChanged || startTimeArrived {
                    if job.startTime != nil { hasRecordedStartTime = true }
                    recordToHistory {
                        try historyStore.recordStatus(jobID: jobID, status: job.status, startedAt: job.startTime)
                    }
                }
            } catch is CancellationError {
                return
            } catch let error as APIClientError {
                // A poll cancelled mid-flight (the cover was dismissed) surfaces as
                // `URLError(.cancelled)` → `APIClientError.transport`, *not* `CancellationError`,
                // so catch it here before it's counted as a failed poll.
                if Task.isCancelled { return }
                consecutiveFailures += 1
                switch classify(error, consecutiveFailures: consecutiveFailures) {
                case .giveUp(let failure):
                    // Record what the last poll error was, whatever the classification — a `.lost`
                    // row is as worth explaining as a `.pollError` one.
                    lastClientErrorText = String(describing: error)
                    transition(to: .failed(failure))
                    return
                case .retry:
                    break // fall through to the wait, then poll again
                }
            } catch {
                // Unreachable: the client only ever throws `APIClientError`. Kept exhaustive so an
                // unexpected throw is asserted in debug and still surfaced (not swallowed).
                assertionFailure("unexpected error from API client: \(error)")
                lastClientErrorText = String(describing: error)
                transition(to: .failed(.pollError(.decoding(String(describing: error)))))
                return
            }

            if ContinuousClock.now >= deadline {
                transition(to: .timedOut)
                return
            }

            do {
                try await Task.sleep(for: config.interval)
            } catch {
                return // CancellationError from the sleep — the screen is going away
            }
        }
    }

    private enum PollOutcome {
        case retry
        case giveUp(Failure)
    }

    /// Decides what a failed poll means: keep trying, or stop.
    private func classify(_ error: APIClientError, consecutiveFailures: Int) -> PollOutcome {
        switch error {
        case .unauthorized, .missingAPIKey, .validation:
            // None of these will be different on the next poll.
            return .giveUp(.pollError(error))
        case .server(let status) where status == 404:
            // We hold a job id the server issued moments ago in the POST response, so a 404 means
            // the in-memory job store lost it (an API restart, per ADR 0013) — or, defensively, a
            // bad id. Tolerate one blip, then treat the job as gone.
            return consecutiveFailures >= 2 ? .giveUp(.lost) : .retry
        case .rateLimited, .transport, .decoding, .server:
            // Transient: a dropped connection, a 5xx, or a 429 (unexpected — the status endpoint
            // is rate-limit-exempt server-side, so treat it as a blip too). One bad poll mustn't
            // kill the screen; give up only once the consecutive-failure budget is spent.
            return consecutiveFailures >= config.maxConsecutiveFailures
                ? .giveUp(.pollError(error))
                : .retry
        }
    }

    // MARK: - History mirror

    /// The single place `phase` is set. Also folds the new phase down to a `JobDisposition` and
    /// writes it to local history, so the screen and the durable record never drift. `job` is the
    /// status poll (or initial job) that caused the change — its `endTime`/`trackId` land on the
    /// row for a terminal outcome.
    private func transition(to newPhase: Phase, job: GenerationJob? = nil) {
        phase = newPhase

        let disposition: JobDisposition
        switch newPhase {
        case .polling:
            // Not actually reachable — `transition` is only ever called with a terminal phase —
            // but the switch has to be exhaustive and `.inFlight` is the honest mapping.
            disposition = .inFlight
        case .ready:
            disposition = .ready
        case .timedOut:
            disposition = .timedOut
        case .failed(let failure):
            switch failure {
            case .jobFailed: disposition = .serverFailed
            case .lost: disposition = .lost
            case .pollError: disposition = .pollError
            }
        }

        // Prefer the job carried by `.ready` (it has the finished `trackId`).
        let contextJob: GenerationJob? = if case .ready(let readyJob) = newPhase { readyJob } else { job }

        recordToHistory {
            try historyStore.recordDisposition(
                jobID: jobID,
                disposition,
                job: contextJob,
                error: lastClientErrorText
            )
        }
    }

    /// Runs a history write, swallowing any failure — persistence mirrors the poll loop, it isn't
    /// part of it, so a store error must never derail the screen (that invariant is the whole
    /// point of ADR 0024). Logged, not trapped, so the debug build behaves like release here.
    private func recordToHistory(_ write: () throws -> Void) {
        do {
            try write()
        } catch {
            #if DEBUG
            print("⚠️ job-history write failed (ignored): \(error)")
            #endif
        }
    }
}

#if DEBUG
extension GenerationProgressViewModel {
    /// A model parked in a specific phase for `#Preview`s. For the `.polling` previews the view's
    /// `.task` still runs the loop, so the stub client answers with a held non-terminal status
    /// and the config uses a very long interval — one harmless poll, then it parks until the
    /// preview is torn down. The terminal previews never poll (`startPolling` returns at its
    /// guard).
    static func preview(
        _ phase: Phase,
        latestStatus: JobStatus = .inProgress
    ) -> GenerationProgressViewModel {
        let model = GenerationProgressViewModel(
            job: .preview(status: .pending),
            styleSummary: "Blues — slow, brushes not sticks",
            apiClient: PreviewStatusClient(held: latestStatus),
            config: PollingConfiguration(
                interval: .seconds(3600),
                timeout: .seconds(3600),
                maxConsecutiveFailures: 4
            ),
            historyStore: PreviewJobHistoryStore()
        )
        // `phase`/`latestStatus` are `private(set)`; this extension is in the same file, so it can
        // set them to stage a preview.
        model.phase = phase
        model.latestStatus = latestStatus
        return model
    }
}

/// Stand-in `BackingTrackAPIClient` for previews — answers every status poll with `held`.
private struct PreviewStatusClient: BackingTrackAPIClient {
    var held: JobStatus = .pending

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        fatalError("the progress screen never submits")
    }
    func jobStatus(id: UUID) async throws -> GenerationJob {
        .preview(status: held)
    }
}
#endif
