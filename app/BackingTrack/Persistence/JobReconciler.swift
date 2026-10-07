import Foundation
import os

// File-level so the struct's memberwise init is unchanged.
private let log = Logger(subsystem: "com.andrewsavory.backingtrack", category: "job_reconciler")

/// Re-checks a stored history row against the server once and folds the result back into the store.
///
/// The history screen's counterpart to `GenerationProgressViewModel`'s poll loop. Dismissing the
/// progress screen before a terminal state is the *normal* action (ADR 0024), so most rows sit at
/// `.inFlight` with a stale status until something re-polls them — that's this.
///
/// It's a *one-shot* check (no failure budget, no deadline, no repeated polling), so its error
/// handling is deliberately gentler than `GenerationProgressViewModel.classify`:
///
/// - A network or server error never changes the row's disposition. A one-shot check that can't
///   reach the API says nothing about the job — the row stays as it was and the caller shows a
///   transient "couldn't refresh". The live loop, with a user watching and a budget, eventually
///   gives up and writes `.pollError`; here we don't.
/// - An auth error (absent or rejected key) is treated the same as unreachable, *not* a permanent
///   `.pollError`: a manual refresh must not brand a historical row over a fixable API key.
/// - Only a sustained 404 is terminal — the in-memory job store dropped the job, typically an API
///   restart (ADR 0013). One 404 is tolerated with a single re-check, mirroring the live loop's
///   one-blip tolerance.
///
/// `reconcile` never throws: a store-write failure is swallowed, like everywhere else the store
/// is touched (ADR 0024).
@MainActor
struct JobReconciler {

    /// What a reconcile did. Only `.advanced` and `.lost` persist a disposition; `.unchanged` may
    /// still have refreshed the row's status.
    enum Outcome: Equatable {
        /// The server says the job is still pending or running — status and `startedAt` refreshed,
        /// disposition untouched (`.inFlight` or `.timedOut`) — or the job is gone but the row
        /// already holds a finished track, so nothing is written.
        case unchanged
        /// The job reached a terminal state — `.ready` or `.serverFailed` written, with the
        /// server's `trackId` / `endTime` folded in.
        case advanced(JobDisposition)
        /// A sustained 404: the job is gone from the server and no `trackId` was ever captured, so
        /// there's nothing to recover. Written as `.lost` — rendered as "may have finished, can't
        /// confirm", not a failure.
        case lost
        /// Couldn't get a usable answer (offline, 5xx, rate limit, rejected key, cancelled). No
        /// disposition written; the caller surfaces this transiently.
        case unreachable(APIClientError)
        /// The row carries no valid job id — nothing to reconcile.
        case notInHistory
    }

    var apiClient: any BackingTrackAPIClient
    var historyStore: any JobHistoryStore
    /// Gap before the single re-check after a first 404. A property so tests don't wait a real
    /// second; production always uses the default.
    var notFoundRetryDelay: Duration = .seconds(1)

    func reconcile(entry: JobHistoryEntry) async -> Outcome {
        guard let jobID = entry.jobID else { return .notInHistory }

        switch await poll(jobID) {
        case .success(let job):
            return apply(job, jobID: jobID)

        case .failure(.server(status: 404)):
            // Tolerate one blip — the live loop rides these out because it re-polls in 1–2s.
            try? await Task.sleep(for: notFoundRetryDelay)
            if Task.isCancelled { return .unreachable(.transport(URLError(.cancelled))) }

            switch await poll(jobID) {
            case .success(let job):
                return apply(job, jobID: jobID)
            case .failure(.server(status: 404)):
                // The job store is in-memory (ADR 0013) but track files aren't (ADR 0014), so a 404
                // says nothing about a track we already recorded — "Check again" on a finished row
                // would otherwise relabel it `.lost` after every API restart.
                if entry.disposition == .ready || entry.trackID != nil { return .unchanged }
                record {
                    try historyStore.recordDisposition(
                        jobID: jobID, .lost, job: nil,
                        error: "The server no longer has this job — most likely an API restart."
                    )
                }
                return .lost
            case .failure(let error):
                return .unreachable(error)
            }

        case .failure(let error):
            return .unreachable(error)
        }
    }

    private func poll(_ jobID: UUID) async -> Result<GenerationJob, APIClientError> {
        do {
            return .success(try await apiClient.jobStatus(id: jobID))
        } catch let error as APIClientError {
            return .failure(error)
        } catch {
            // The client only ever throws `APIClientError` (a cancelled request included, as
            // `.transport(URLError(.cancelled))`). Anything else is unexpected — surface it as
            // unreachable rather than crashing a background refresh.
            assertionFailure("unexpected error from API client: \(error)")
            return .failure(.decoding(String(describing: error)))
        }
    }

    private func apply(_ job: GenerationJob, jobID: UUID) -> Outcome {
        guard job.status.isTerminal else {
            record {
                try historyStore.recordStatus(jobID: jobID, status: job.status, startedAt: job.startTime)
            }
            return .unchanged
        }
        let disposition: JobDisposition = if job.status.isSuccess {
            .ready
        } else if job.status == .cancelled {
            .cancelled
        } else {
            .serverFailed
        }
        record { try historyStore.recordDisposition(jobID: jobID, disposition, job: job, error: nil) }
        return .advanced(disposition)
    }

    private func record(_ write: () throws -> Void) {
        do {
            try write()
        } catch {
            #if DEBUG
            log.warning("job-history reconcile write failed (ignored): \(error)")
            #endif
        }
    }
}
