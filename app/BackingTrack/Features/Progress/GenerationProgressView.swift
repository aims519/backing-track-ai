import SwiftUI

/// Full-screen cover shown the moment `POST /tracks/generate` accepts a job. Polls the status
/// endpoint (via `GenerationProgressViewModel`) and walks the user from "Queued…" through to a
/// "Track ready" or failure state.
///
/// Presented with `.fullScreenCover` rather than a navigation push: generation is a modal
/// interruption of the form, and there's no half-submitted state to swipe back to. The toolbar
/// button ("Cancel" while polling, "Done" once terminal) dismisses back to the still-populated
/// form; cancelling just stops polling — the backend job has no cancel endpoint and may still
/// finish (ADR 0023).
struct GenerationProgressView: View {
    @State private var model: GenerationProgressViewModel
    @Environment(\.dismiss) private var dismiss

    init(model: GenerationProgressViewModel) {
        _model = State(initialValue: model)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                phaseContent
                    .padding(.vertical, 16)
                    .frame(maxWidth: .infinity)
            }
            .background(Color(.systemGroupedBackground))
            .navigationTitle(navigationTitle)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if model.phase.isTerminal {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Done") { dismiss() }
                    }
                } else {
                    // A generation in progress must still be abandonable — the pre-cover flow let
                    // the user dismiss the acceptance alert immediately.
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Cancel") { dismiss() }
                    }
                }
            }
        }
        .task { await model.startPolling() }
        .interactiveDismissDisabled()
        // VoiceOver: announce the caption on a status change *while polling* (a terminal status is
        // covered by the phase change below, and its caption would be the misleading "Finishing
        // up…"), and the outcome when the phase settles.
        .onChange(of: model.latestStatus) {
            if case .polling = model.phase { announce(model.progressCaption) }
        }
        .onChange(of: model.phase) { _, phase in announcePhaseChange(phase) }
    }

    // MARK: - Phase content

    @ViewBuilder
    private var phaseContent: some View {
        switch model.phase {
        case .polling:
            pollingView
        case .ready(let job):
            readyView(job)
        case .failed(let failure):
            failureView(failure)
        case .timedOut:
            timedOutView
        }
    }

    private var pollingView: some View {
        VStack(spacing: 22) {
            ProgressView()
                .controlSize(.large)
                .tint(Color.accentColor)

            Text(model.progressCaption)
                .font(.title3)
                .contentTransition(.opacity)

            // Anchor the tick schedule on the fixed origin, not `.now` — `.now` is re-read on
            // every body evaluation (each poll re-renders this view), which would restart the
            // 1s cadence and make the timer stutter.
            TimelineView(.periodic(from: model.elapsedOrigin, by: 1)) { context in
                Text(elapsed(at: context.date))
                    .font(.system(.body, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            // Announced through `progressCaption` changes instead — a value that updates every
            // second is noise for VoiceOver.
            .accessibilityHidden(true)

            trackSummaryCard
        }
    }

    private func readyView(_ job: GenerationJob) -> some View {
        VStack(spacing: 22) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 56))
                .foregroundStyle(Color.accentColor)
                .accessibilityHidden(true)

            Group {
                if let duration = generationDuration(of: job) {
                    Text("Track generated in \(duration)")
                } else {
                    Text("Track ready")
                }
            }
            .font(.title3.bold())

            if job.status == .completedViaFallback {
                Text("Arranged with the built-in fallback rather than the model.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 24)
            }

            trackSummaryCard

            VStack(spacing: 6) {
                Button {
                    // Playback lands with the player screen — a later task.
                } label: {
                    Label("Play", systemImage: "play.fill")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(true)
                .accessibilityHint("Not available yet")

                Text("Playback arrives with the player screen.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 16)
        }
    }

    private func failureView(_ failure: GenerationProgressViewModel.Failure) -> some View {
        messageView(
            systemImage: "exclamationmark.triangle",
            title: "Couldn't generate the track",
            message: failureMessage(failure)
        )
    }

    private var timedOutView: some View {
        messageView(
            systemImage: "clock.badge.exclamationmark",
            title: "This is taking longer than expected",
            message: "Your track may still be generating on the server. You'll be able to check on "
                + "it once the track library screen exists."
        )
    }

    /// Shared layout for the failed / timed-out states — a muted icon, a title and a sentence.
    private func messageView(systemImage: String, title: String, message: String) -> some View {
        VStack(spacing: 16) {
            Image(systemName: systemImage)
                .font(.system(size: 48))
                .foregroundStyle(.secondary)
                .accessibilityHidden(true)

            Text(title)
                .font(.title3.weight(.semibold))
                .multilineTextAlignment(.center)

            Text(message)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(.horizontal, 24)
        .frame(maxWidth: .infinity)
    }

    // MARK: - Track summary

    /// The requested parameters, echoed from the job so the user can see what's being generated.
    private var trackSummaryCard: some View {
        SectionCard("Track") {
            VStack(spacing: 0) {
                summaryRow("Key Root", model.initialJob.keyRoot)
                rowDivider
                summaryRow("Tempo", "\(model.initialJob.tempo) BPM")
                rowDivider
                summaryRow(
                    "Time",
                    "\(model.initialJob.timeSignatureBeatCount)/\(model.initialJob.timeSignatureBeatUnit)"
                )
                if let style = model.styleSummary, !style.isEmpty {
                    rowDivider
                    summaryRow("Style", style)
                }
                rowDivider
                summaryRow("Chords", model.initialJob.chords.joined(separator: "  "))
            }
        }
    }

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

    // MARK: - Helpers

    private var navigationTitle: String {
        switch model.phase {
        case .polling: "Generating"
        case .ready: "Track Ready"
        case .failed: "Generation Failed"
        case .timedOut: "Still Working"
        }
    }

    /// Formats a non-negative number of seconds as `m:ss`. Clamped at zero — these come from
    /// server timestamps, and the server's clock can be slightly ahead of the device's.
    private func clockString(_ seconds: TimeInterval) -> String {
        let whole = max(0, Int(seconds.rounded()))
        return String(format: "%d:%02d", whole / 60, whole % 60)
    }

    /// Live time since the job started (or since the screen opened, if the server gave no start),
    /// ticked once a second by the polling view's `TimelineView`.
    private func elapsed(at now: Date) -> String {
        clockString(now.timeIntervalSince(model.elapsedOrigin))
    }

    /// How long generation actually took, from the finished job's own timestamps. `nil` if either
    /// timestamp is missing (shouldn't happen for a terminal job, but the DTO fields are optional).
    private func generationDuration(of job: GenerationJob) -> String? {
        guard let start = job.startTime, let end = job.endTime else { return nil }
        return clockString(end.timeIntervalSince(start))
    }

    private func failureMessage(_ failure: GenerationProgressViewModel.Failure) -> String {
        switch failure {
        case .jobFailed:
            "The track couldn't be generated. Try adjusting the chords or style, then generate again."
        case .lost:
            "The server restarted and lost track of this job. Generate again to retry."
        case .pollError(let error):
            error.errorDescription
                ?? "Lost contact with the server while the track was generating."
        }
    }

    private func announce(_ message: String) {
        AccessibilityNotification.Announcement(message).post()
    }

    private func announcePhaseChange(_ phase: GenerationProgressViewModel.Phase) {
        switch phase {
        case .polling:
            break
        case .ready:
            announce("Track ready")
        case .failed(let failure):
            announce(failureMessage(failure))
        case .timedOut:
            announce("Generation is taking longer than expected")
        }
    }
}

#Preview("Polling – queued") {
    GenerationProgressView(model: .preview(.polling, latestStatus: .pending))
}

#Preview("Polling – in progress") {
    GenerationProgressView(model: .preview(.polling, latestStatus: .inProgress))
}

#Preview("Ready") {
    GenerationProgressView(model: .preview(.ready(.preview(status: .complete))))
}

#Preview("Ready – fallback") {
    GenerationProgressView(model: .preview(.ready(.preview(status: .completedViaFallback))))
}

#Preview("Failed") {
    GenerationProgressView(model: .preview(.failed(.jobFailed)))
}

#Preview("Timed out") {
    GenerationProgressView(model: .preview(.timedOut))
}
