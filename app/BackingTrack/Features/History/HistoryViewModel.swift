import Foundation
import Observation

/// Backing state for `HistoryView` — the non-`@Query` side of the screen: reconciling stale rows
/// against the server, deleting, and running the "Watch progress" cover.
///
/// The list itself comes from `@Query` (a View-only wrapper), so this model never holds the
/// entries; callers hand it the current snapshot when they need a pass run.
@MainActor
@Observable
final class HistoryViewModel {

    @ObservationIgnored private let apiClient: any BackingTrackAPIClient
    @ObservationIgnored private let historyStore: any JobHistoryStore
    @ObservationIgnored private let reconciler: JobReconciler

    /// True while a reconcile pass is running — drives the list's refresh affordance and guards
    /// against `.task` and `.refreshable` overlapping.
    private(set) var isReconciling = false

    /// Set when a pass couldn't reach the server. Transient: shown as a top banner and cleared at
    /// the start of the next pass. Not an alert — a failed refresh isn't worth interrupting for.
    var refreshError: String?

    /// Drives the "Watch progress" `fullScreenCover`. Non-`nil` while it's presented.
    var watchSeed: GenerationJob?
    @ObservationIgnored private var watchStyleSummary: String?
    /// Memoised like `GenerateTrackViewModel.makeProgressViewModel(for:)` — SwiftUI re-evaluating
    /// the cover's content closure must not spin up a second poll loop.
    @ObservationIgnored private var watchModel: GenerationProgressViewModel?

    init(
        apiClient: any BackingTrackAPIClient = LiveBackingTrackAPIClient(),
        historyStore: any JobHistoryStore = SwiftDataJobHistoryStore()
    ) {
        self.apiClient = apiClient
        self.historyStore = historyStore
        self.reconciler = JobReconciler(apiClient: apiClient, historyStore: historyStore)
    }

    // MARK: - Reconcile

    /// One-shot reconcile of every `.inFlight` / `.timedOut` row, in order. Safe to call from both
    /// `.task` and `.refreshable`: a second concurrent call early-returns. The row currently being
    /// watched is skipped — its own poll loop owns it.
    func reconcileVisible(_ entries: [JobHistoryEntry]) async {
        guard !isReconciling else { return }
        isReconciling = true
        defer { isReconciling = false }
        refreshError = nil

        let watchedID = watchSeed?.id
        let targets = entries.filter { entry in
            guard entry.disposition == .inFlight || entry.disposition == .timedOut else { return false }
            return entry.jobID != watchedID
        }
        for entry in targets {
            if Task.isCancelled { return }
            note(await reconciler.reconcile(entry: entry))
        }
    }

    /// Keeps re-checking the `.inFlight` rows in `entries` every `interval` until none are left or
    /// the task is cancelled — the dashboard banner's refresh loop, run from a `.task` so
    /// leaving the screen cancels it.
    ///
    /// Reuses `reconcileVisible` rather than a second poll loop, and only ever hands it `.inFlight`
    /// rows: `.timedOut` rows are skipped here, or this would poll each of them forever. The
    /// disposition is re-read from the live `@Model` on every pass, so a row that finished during
    /// the previous one drops out even if the caller hasn't re-run the loop yet.
    func keepReconcilingInFlight(_ entries: [JobHistoryEntry], every interval: Duration) async {
        while !Task.isCancelled {
            let inFlight = entries.filter { $0.disposition == .inFlight }
            guard !inFlight.isEmpty else { return }
            await reconcileVisible(inFlight)
            do {
                try await Task.sleep(for: interval)
            } catch {
                return // CancellationError — the screen is going away
            }
        }
    }

    /// Re-check one row on demand — any disposition, so it doubles as the escape hatch for a
    /// `.lost` row (an API restart came back) or a `.pollError` one (a since-fixed key).
    func checkAgain(_ entry: JobHistoryEntry) async {
        note(await reconciler.reconcile(entry: entry))
    }

    private func note(_ outcome: JobReconciler.Outcome) {
        guard case .unreachable(let error) = outcome else { return }
        // A poll cut short by the screen going away isn't a refresh failure worth surfacing.
        if case .transport(let urlError) = error, urlError.code == .cancelled { return }
        refreshError = Self.refreshErrorMessage(error)
    }

    // MARK: - Delete

    func delete(_ entry: JobHistoryEntry) {
        guard let jobID = entry.jobID else { return }
        delete(jobID: jobID)
    }

    /// Deletes by id — the detail view uses this from `.onDisappear`, once it's no longer holding
    /// (or rendering) the row, so the delete can't trip a re-render of a destroyed `@Model`.
    func delete(jobID: UUID) {
        do {
            try historyStore.delete(jobID: jobID)
        } catch {
            #if DEBUG
            print("⚠️ history delete failed (ignored): \(error)")
            #endif
        }
    }

    // MARK: - Watch progress

    func startWatching(_ entry: JobHistoryEntry) {
        guard let seed = GenerationJob.seed(from: entry) else { return }
        watchStyleSummary = entry.style.isEmpty ? nil : entry.style
        watchSeed = seed
    }

    func stopWatching() {
        watchSeed = nil
        watchStyleSummary = nil
        watchModel = nil
    }

    func progressViewModel(for seed: GenerationJob) -> GenerationProgressViewModel {
        if let watchModel, watchModel.initialJob.id == seed.id { return watchModel }
        let model = GenerationProgressViewModel(
            job: seed,
            styleSummary: watchStyleSummary,
            apiClient: apiClient,
            historyStore: historyStore
        )
        watchModel = model
        return model
    }

    // MARK: - Copy

    private static func refreshErrorMessage(_ error: APIClientError) -> String {
        switch error {
        case .missingAPIKey:
            "No API key set — add one in Settings to check for updates."
        case .unauthorized:
            "The API key was rejected — check it in Settings."
        default:
            "Couldn't reach the server. Pull down to try again."
        }
    }
}
