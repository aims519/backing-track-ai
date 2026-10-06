import SwiftData
import SwiftUI

/// The dashboard's latest generations, newest first, each opening that job's detail view. Every
/// disposition is shown (not just finished tracks): `HistoryRow`'s trailing icon already says what
/// became of a job, and hiding failures would make the dashboard disagree with History.
struct RecentTracksSection: View {
    /// Enough to orient someone without turning the dashboard into the history list.
    static let limit = 5

    @Query private var entries: [JobHistoryEntry]

    init() {
        var descriptor = FetchDescriptor<JobHistoryEntry>(
            sortBy: [SortDescriptor(\.createdAt, order: .reverse)]
        )
        descriptor.fetchLimit = Self.limit
        _entries = Query(descriptor)
    }

    var body: some View {
        if entries.isEmpty {
            emptyState
        } else {
            // A custom header rather than `SectionCard`'s uppercase caption, which has no room for
            // a trailing link. Kept local instead of adding a `SectionCard` variant that its ten
            // other callers would all share.
            VStack(alignment: .leading, spacing: 8) {
                header

                SectionCard {
                    VStack(spacing: 0) {
                        ForEach(entries) { entry in
                            NavigationLink(value: entry) {
                                HistoryRow(entry: entry)
                                    .padding(.horizontal, 16)
                                    .padding(.vertical, 10)
                                    .contentShape(.rect)
                            }
                            // Plain, or the row's text takes the accent colour a bare link applies.
                            .buttonStyle(.plain)

                            // No divider under the last row: nothing follows it inside the card now.
                            if entry != entries.last {
                                Divider().padding(.leading, 16)
                            }
                        }
                    }
                }
            }
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            Text("Recent")
                .font(.title3.weight(.semibold))
                .accessibilityAddTraits(.isHeader)
            Spacer()
            NavigationLink("See all", value: HistoryRoute.list)
                .font(.subheadline)
                // The visible text is terse; VoiceOver should still say what it opens.
                .accessibilityLabel("See all history")
        }
        // Matches `SectionCard`'s own header inset, so the title lines up with the card's text.
        .padding(.horizontal, 20)
    }

    private var emptyState: some View {
        SectionCard {
            VStack(spacing: 16) {
                Image(systemName: "music.note.list")
                    .font(.system(size: 44))
                    .foregroundStyle(.tertiary)
                    .accessibilityHidden(true)

                VStack(spacing: 6) {
                    Text("Nothing yet")
                        .font(.title3.weight(.semibold))
                    Text("Tap 'New Track' to start generating")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 32)
            .frame(maxWidth: .infinity)
        }
    }
}

#if DEBUG
/// An in-memory store with a few rows in different states, for `#Preview`s.
@MainActor
func previewHomeContainer(populated: Bool = true) -> ModelContainer {
    let container = try! ModelContainer(
        for: JobHistoryEntry.self,
        configurations: ModelConfiguration(isStoredInMemoryOnly: true)
    )
    guard populated else { return container }
    let now = Date()
    let rows = [
        JobHistoryEntry.preview(createdAt: now, disposition: .ready, style: "Blues - slow shuffle"),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-2 * 3600),
            status: .inProgress, disposition: .inFlight, style: "Rock"
        ),
        JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-26 * 3600),
            disposition: .serverFailed, style: "Funk - on the one"
        ),
    ]
    rows.forEach(container.mainContext.insert)
    return container
}

#Preview("Populated") {
    NavigationStack {
        ScrollView { RecentTracksSection().padding(.vertical, 16) }
            .background(Color(.systemGroupedBackground))
    }
    .modelContainer(previewHomeContainer())
}

#Preview("Empty") {
    NavigationStack {
        ScrollView { RecentTracksSection().padding(.vertical, 16) }
            .background(Color(.systemGroupedBackground))
    }
    .modelContainer(previewHomeContainer(populated: false))
}
#endif
