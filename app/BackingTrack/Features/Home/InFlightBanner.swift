import SwiftData
import SwiftUI

/// The dashboard's "something is generating" banner. Absent when nothing is running; otherwise
/// it names the job being worked on (the oldest unfinished one — see `InFlightSummary`), with a
/// "+N more" for any queued behind it, and tapping it reopens that job's progress screen.
///
/// Dismissing the progress screen early is the normal action (ADR 0024), which stops its poll
/// loop and leaves the row's status stale. The `.task` below is what keeps the banner honest while
/// the dashboard is on screen: it re-reconciles the in-flight rows every few seconds. When a job
/// finishes the banner simply disappears and the job shows up, finished, in the recent list.
struct InFlightBanner: View {
    /// Gentler than the progress screen's 1-2s: a banner glanced at from the dashboard doesn't
    /// need to be that fresh. Push notifications (ADR 0039) cover the app being backgrounded.
    static let refreshInterval: Duration = .seconds(4)

    let vm: HistoryViewModel

    @Query private var entries: [JobHistoryEntry]

    init(vm: HistoryViewModel) {
        self.vm = vm
        // `#Predicate` filters on the stored string, not the computed `disposition` (see
        // `JobHistoryEntry`), captured first because a predicate can't call into the enum.
        let inFlight = JobDisposition.inFlight.rawValue
        _entries = Query(
            filter: #Predicate<JobHistoryEntry> { $0.dispositionRawValue == inFlight },
            sort: \.createdAt,
            order: .reverse
        )
    }

    var body: some View {
        Group {
            if let summary = InFlightSummary(entries: entries) {
                Button {
                    vm.startWatching(summary.headline)
                } label: {
                    card(for: summary)
                }
                .buttonStyle(.plain)
                .accessibilityHint("Shows progress")
            }
        }
        // Keyed on which jobs are running so the loop restarts with a fresh snapshot when one
        // finishes or a new one arrives, rather than closing over a stale `entries`. With nothing
        // in flight the loop returns at once. Cancelled when the dashboard is covered or popped.
        .task(id: entries.map(\.jobIDString)) {
            await vm.keepReconcilingInFlight(entries, every: Self.refreshInterval)
        }
    }

    private func card(for summary: InFlightSummary) -> some View {
        SectionCard {
            HStack(spacing: 12) {
                ProgressView()
                VStack(alignment: .leading, spacing: 2) {
                    Text(summary.headline.status.progressCaption)
                        .font(.headline)
                    Text(subtitle(for: summary))
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                Image(systemName: "chevron.right")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
            .padding(16)
            .contentShape(.rect)
        }
        .accessibilityElement(children: .combine)
    }

    private func subtitle(for summary: InFlightSummary) -> String {
        let style = summary.headline.style.isEmpty ? "Untitled" : summary.headline.style
        guard summary.othersCount > 0 else { return style }
        return "\(style) · +\(summary.othersCount) more"
    }
}

#if DEBUG
#Preview("Generating") {
    let container = previewHomeContainer(populated: false)
    container.mainContext.insert(
        JobHistoryEntry.preview(status: .inProgress, disposition: .inFlight, style: "Blues - slow shuffle")
    )
    return ScrollView {
        InFlightBanner(vm: HistoryViewModel(historyStore: PreviewJobHistoryStore()))
            .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
    .modelContainer(container)
}
#endif
