import SwiftUI
import SwiftData

/// Route for pushing the history screen onto `HomeView`'s navigation stack.
enum HistoryRoute: Hashable {
    case list
}

/// The job-history screen: a day-grouped list of every generation the app has submitted, read
/// live from SwiftData via `@Query`. On open and on pull-to-refresh it reconciles the rows the
/// client never saw finish (`.inFlight` / `.timedOut`) against the server — ADR 0024's
/// "reconcile on open" requirement.
///
/// A plain list: tapping a row pushes a `JobHistoryEntry`, whose detail destination — and the
/// "Watch progress" cover — are registered by `HomeView`, which owns the navigation stack and
/// shares this screen's `HistoryViewModel` with the dashboard.
struct HistoryView: View {
    // `@Query` reads the container `BackingTrackApp` puts in the environment; its writes and the
    // reconcile/delete writes go through the same `mainContext`, so the list stays in sync.
    @Query(sort: \JobHistoryEntry.createdAt, order: .reverse)
    private var entries: [JobHistoryEntry]

    let vm: HistoryViewModel

    private var sections: [HistorySection] {
        HistoryGrouping.sections(from: entries)
    }

    var body: some View {
        List {
            ForEach(sections) { section in
                Section(section.title) {
                    ForEach(section.entries) { entry in
                        NavigationLink(value: entry) {
                            HistoryRow(entry: entry)
                        }
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                vm.delete(entry)
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("History")
        .navigationBarTitleDisplayMode(.inline)
        // Keep the List mounted underneath so `.refreshable` still has a scroll view to hang off.
        .overlay {
            if entries.isEmpty { HistoryEmptyState() }
        }
        .task { await vm.reconcileVisible(entries) }
        .refreshable { await vm.reconcileVisible(entries) }
        .safeAreaInset(edge: .top) {
            if let message = vm.refreshError {
                RefreshErrorBanner(message: message) { vm.refreshError = nil }
            }
        }
    }
}

/// The transient "couldn't refresh" strip shown at the top of the list after a failed reconcile
/// pass. Dismissed by hand or replaced at the start of the next pass.
private struct RefreshErrorBanner: View {
    let message: String
    let onDismiss: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
            Text(message).font(.footnote)
            Spacer(minLength: 8)
            Button(action: onDismiss) {
                Image(systemName: "xmark").font(.footnote.weight(.semibold))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal)
        .padding(.vertical, 10)
        .frame(maxWidth: .infinity)
        .background(.orange.opacity(0.15))
        .foregroundStyle(.orange)
    }
}

#if DEBUG
/// A `BackingTrackAPIClient` for `#Preview`s — answers every poll with a running job, so the
/// reconcile pass never touches the network.
private struct PreviewAPIClient: BackingTrackAPIClient {
    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        fatalError("history previews never submit")
    }
    func jobStatus(id: UUID) async throws -> GenerationJob {
        .preview(id: id, status: .inProgress)
    }
    func cancelJob(id: UUID) async throws {}
    func downloadTrackData(trackID: UUID) async throws -> Data {
        fatalError("history previews never download")
    }
}

@MainActor
private func previewHistoryContainer() -> ModelContainer {
    let container = try! ModelContainer(
        for: JobHistoryEntry.self,
        configurations: ModelConfiguration(isStoredInMemoryOnly: true)
    )
    let now = Date()
    let rows = [
        JobHistoryEntry.preview(createdAt: now, disposition: .ready, style: "Blues - slow shuffle"),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-2 * 3600),
            status: .inProgress, disposition: .inFlight, style: "Rock"
        ),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-26 * 3600),
            disposition: .timedOut, style: "Funk - on the one"
        ),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-30 * 3600),
            disposition: .lost, style: "Reggae"
        ),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-5 * 86_400),
            disposition: .serverFailed, style: "Jazz - brushes"
        ),
    ]
    rows.forEach(container.mainContext.insert)
    return container
}

#Preview("Populated") {
    NavigationStack {
        HistoryView(
            vm: HistoryViewModel(apiClient: PreviewAPIClient(), historyStore: PreviewJobHistoryStore())
        )
    }
    .modelContainer(previewHistoryContainer())
}

#Preview("Empty") {
    NavigationStack {
        HistoryView(
            vm: HistoryViewModel(apiClient: PreviewAPIClient(), historyStore: PreviewJobHistoryStore())
        )
    }
    .modelContainer(for: JobHistoryEntry.self, inMemory: true)
}
#endif
