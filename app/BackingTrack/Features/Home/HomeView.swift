import SwiftUI

/// The app's landing screen and the owner of its navigation: a "New Track" button and the most
/// recent generations, with the form, history list, job detail and player pushed on top.
///
/// The `GenerateTrackViewModel` and `HistoryViewModel` live here rather than in the screens they
/// back. A pushed `GenerateTrackView` is rebuilt every time it's popped and pushed again, so a
/// model owned by it would lose a half-built progression on "back"; owned here, the form survives,
/// and "Use these settings" can seed it before pushing the screen.
struct HomeView: View {
    @State private var model: GenerateTrackViewModel
    @State private var historyVM: HistoryViewModel
    // Optional so previews (which have no app delegate) don't need one injected.
    @Environment(NotificationRouter.self) private var notificationRouter: NotificationRouter?

    // Empty = the dashboard. Everything else is pushed onto it.
    @State private var path = NavigationPath()
    @State private var isShowingSettings = false

    init(
        model: GenerateTrackViewModel = GenerateTrackViewModel(),
        historyVM: HistoryViewModel = HistoryViewModel()
    ) {
        _model = State(initialValue: model)
        _historyVM = State(initialValue: historyVM)
    }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(spacing: 22) {
                    newTrackButton
                    RecentTracksSection()
                }
                .padding(.vertical, 16)
            }
            .background(Color(.systemGroupedBackground))
            .navigationTitle("Backing Tracks")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        isShowingSettings = true
                    } label: {
                        Label("Settings", systemImage: "gearshape")
                    }
                }
            }
            .navigationDestination(for: GenerateRoute.self) { _ in
                GenerateTrackView(model: model)
            }
            .navigationDestination(for: HistoryRoute.self) { _ in
                HistoryView(vm: historyVM)
            }
            // Registered here, not on `HistoryView`, so a job opened from the dashboard's recent
            // list and one opened from the full history list share one destination.
            .navigationDestination(for: JobHistoryEntry.self) { entry in
                HistoryDetailView(entry: entry, vm: historyVM, onReRun: reRun)
            }
            // One registration covers Play links from the job detail view.
            .navigationDestination(for: PlayerRoute.self) { route in
                switch route {
                case .player(let input):
                    PlayerView(model: PlayerViewModel(input: input))
                }
            }
        }
        .sheet(isPresented: $isShowingSettings) {
            SettingsView()
        }
        // "Watch progress" on a still-running job's detail view: the same progress screen the
        // generate flow uses, over the whole window. Attached outside the stack so it can be
        // presented from a detail view pushed from either the dashboard or the history list.
        // `stopWatching` clears the memoised model on dismiss.
        .fullScreenCover(item: $historyVM.watchSeed, onDismiss: historyVM.stopWatching) { seed in
            GenerationProgressView(model: historyVM.progressViewModel(for: seed))
        }
        // `onChange`, not `.task(id:)`: a task is cancelled while a progress cover hides the
        // stack, and that's exactly when a notification tap needs handling. `initial` covers a
        // cold launch, where the tap lands before this view first renders.
        .onChange(of: notificationRouter?.pendingJobID, initial: true) {
            openPendingNotificationJob()
        }
    }

    private var newTrackButton: some View {
        Button {
            path.append(GenerateRoute.new)
        } label: {
            Label("New Track", systemImage: "plus")
                .frame(maxWidth: .infinity)
        }
        .buttonStyle(.borderedProminent)
        .controlSize(.large)
        .padding(.horizontal, 16)
    }

    /// A tapped job-finished notification: History ▸ that job's detail, whose own `.task` refreshes
    /// a stale in-flight row. Falls back to the history list if the job isn't in local history.
    private func openPendingNotificationJob() {
        guard let router = notificationRouter, let jobID = router.pendingJobID else { return }
        router.pendingJobID = nil
        isShowingSettings = false
        // The watch cover is no longer torn down by the path reset below (it isn't part of the
        // stack), so close it explicitly or it would sit over the job we're navigating to.
        historyVM.stopWatching()

        var newPath = NavigationPath()
        newPath.append(HistoryRoute.list)
        if let entry = model.openJobFromNotification(jobID) {
            newPath.append(entry)
        }
        path = newPath
    }

    /// "Use these settings" on a job's detail view: seed the form from that job and land on it,
    /// with the dashboard (not the old detail view) underneath.
    private func reRun(_ entry: JobHistoryEntry) {
        model.apply(entry: entry)
        var newPath = NavigationPath()
        newPath.append(GenerateRoute.new)
        path = newPath
    }
}

#if DEBUG
#Preview {
    HomeView(
        model: GenerateTrackViewModel(historyStore: PreviewJobHistoryStore()),
        historyVM: HistoryViewModel(historyStore: PreviewJobHistoryStore())
    )
    .modelContainer(previewHomeContainer())
}
#endif
