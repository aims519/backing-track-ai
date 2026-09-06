import SwiftUI

struct HistoryDetailView: View {
    let entry: JobHistoryEntry
    let vm: HistoryViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var isCheckingStatus: Bool = false
    @State private var isConfirmingDelete: Bool = false
    @State private var isConfirmingReRun: Bool = false
    /// Set by the Delete button; the row is actually removed in `.onDisappear`, once this view is
    /// off the stack — deleting the `@Model` while it's still in the nav path traps.
    @State private var deletedJobID: UUID?
    let onReRun: (JobHistoryEntry) -> Void
    
    private var rowDivider: some View {
        Divider().padding(.leading, 16)
    }

    private func summaryRow(_ label: String, _ value: String) -> some View {
        CardRow(label) {
            Text(value)
                .foregroundStyle(.secondary)
                .monospacedDigit()
                .multilineTextAlignment(.trailing)
        }
    }
    
    private var trackSummaryCard: some View {
        SectionCard("Request") {
            VStack(spacing: 0) {
                summaryRow("Key Root", entry.keyRoot)
                rowDivider
                summaryRow("Tempo", "\(entry.tempo) BPM")
                rowDivider
                summaryRow(
                    "Time",
                    "\(entry.timeSignatureBeatCount)/\(entry.timeSignatureBeatUnit)"
                )
                rowDivider
                summaryRow("Mode", entry.mode)
                
                rowDivider
                summaryRow("Chords", entry.chordSymbols.joined(separator: "  "))
            }
        }
    }
    
    private var statusCard: some View {
        SectionCard("Status") {
            VStack(spacing: 0) {
                summaryRow("Status", entry.status.displayName)
                rowDivider
                summaryRow("Disposition", entry.disposition.historyLabel)
                rowDivider
                CardRow("Created") {
                    Text(entry.createdAt, format: .dateTime.month().day().hour().minute())
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }
                if let startedAt = entry.startedAt {
                    rowDivider
                    CardRow("Started") {
                        Text(startedAt, format: .dateTime.month().day().hour().minute())
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
                if let finishedAt = entry.finishedAt {
                    rowDivider
                    CardRow("Finished") {
                        Text(finishedAt, format: .dateTime.month().day().hour().minute())
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
                if let start = entry.startedAt, let end = entry.finishedAt {
                    let elapsed = Duration.seconds(end.timeIntervalSince(start))
                    CardRow("Took") {
                        Text(elapsed.formatted(.time(pattern: .minuteSecond)))   // "0:42"
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
            }
        }
    }
    
    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                Text(entry.style).font(.headline)
                Label(entry.disposition.historyLabel, systemImage: entry.disposition.historyIcon)
                    .foregroundStyle(entry.disposition.historyTint)
                
                trackSummaryCard
                statusCard
                if let error = entry.lastClientError,
                   [.serverFailed, .lost, .pollError].contains(entry.disposition) {
                    DisclosureGroup("Details") {
                        Text(error)
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                
                if !entry.status.isTerminal && (entry.disposition == .inFlight || entry.disposition == .timedOut) {
                    Button {
                        vm.startWatching(entry)
                    } label: {
                        Text("Watch progress").frame(maxWidth: .infinity)
                    }
                }

                Button {
                    Task {
                        isCheckingStatus = true
                        await vm.checkAgain(entry)
                        isCheckingStatus = false
                    }
                } label: {
                    Group {
                        if isCheckingStatus { ProgressView() } else { Text("Check again") }
                    }
                    .frame(maxWidth: .infinity)
                }
                .disabled(isCheckingStatus)

                Button {
                    isConfirmingReRun = true
                } label: {
                    Text("Use these settings").frame(maxWidth: .infinity)
                }

                if entry.trackID != nil {
                    Button {} label: {
                        Text("Play").frame(maxWidth: .infinity)
                    }
                    .disabled(true)
                    Text("Playback arrives with the player screen.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Button(role: .destructive) {
                    isConfirmingDelete = true
                } label: {
                    Text("Delete").frame(maxWidth: .infinity)
                }
            }
            .padding()
        }
        .buttonStyle(.bordered)
        .controlSize(.large)
        .background(Color(.systemGroupedBackground))
        // Freshen this one job on open, same as the list does its visible rows — a job that's
        // still running (or that timed out client-side but may have finished) shouldn't show a
        // stale status just because you drilled in before the list's own pass resolved it.
        .task {
            if entry.disposition == .inFlight || entry.disposition == .timedOut {
                await vm.checkAgain(entry)
            }
        }
        .confirmationDialog("Load these settings into the form?", isPresented: $isConfirmingReRun) {
            Button("Load settings") { onReRun(entry) }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog("Delete this generation?", isPresented: $isConfirmingDelete, titleVisibility: .visible) {
            Button("Delete", role: .destructive) {
                deletedJobID = entry.jobID
                dismiss()
            }
            Button("Cancel", role: .cancel) {}
        }
        .onDisappear {
            // The view is off the stack now — the entry is no longer in the nav path or rendered,
            // so removing it can't force a re-render against a destroyed `@Model`.
            if let deletedJobID { vm.delete(jobID: deletedJobID) }
        }
    }
}

#Preview("Ready") {
    HistoryDetailView(entry: JobHistoryEntry.preview(), vm: HistoryViewModel(), onReRun: { _ in })
}

#Preview("Server Failed") {
    HistoryDetailView(entry: JobHistoryEntry.preview(disposition: .serverFailed, lastClientError: "There was a problem connecting to the server"), vm: HistoryViewModel(), onReRun: { _ in })
}

#Preview("Lost") {
    HistoryDetailView(entry: JobHistoryEntry.preview(disposition: .lost, lastClientError: "The job was lost on the server side"), vm: HistoryViewModel(), onReRun: { _ in })
}

#Preview("Poll Error") {
    HistoryDetailView(entry: JobHistoryEntry.preview(disposition: .pollError, lastClientError: "Unable to get the latest job status"), vm: HistoryViewModel(), onReRun: { _ in })
}

#Preview("In Flight") {
    HistoryDetailView(entry: .preview(status: .inProgress, disposition: .inFlight), vm: HistoryViewModel(), onReRun: { _ in })
}

#Preview("In flight — watch") {
    @Previewable @State var vm = HistoryViewModel()
    return NavigationStack {
        HistoryDetailView(entry: .preview(status: .inProgress, disposition: .inFlight),
                          vm: vm, onReRun: { _ in })
    }
    .fullScreenCover(item: $vm.watchSeed, onDismiss: vm.stopWatching) {
        GenerationProgressView(model: vm.progressViewModel(for: $0))
    }
}
