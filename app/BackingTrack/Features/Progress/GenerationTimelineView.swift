import SwiftUI

/// A chronological log of the job's `feedbackItems`, one row per *run* of consecutive same-stage
/// messages the workflow has reported so far.
///
/// Deliberately not a fixed-step progress stepper: `validate`/`repair`/`critic` can loop several
/// times before a job finishes, so a fixed set of steps would either misrepresent progress or need
/// awkward "back to step 3" handling. This just grows as items arrive, like a build log, and reads
/// naturally whether the run was a straight line or a handful of retries.
///
/// Grouping is on *consecutive* stage runs, not stage identity overall: `Validation` appearing
/// again after a `Repair` is a new attempt at the same stage, not a continuation of the earlier
/// one, so it gets its own row rather than merging back into the first `Validation` group.
struct GenerationTimelineView: View {
    let items: [JobFeedback]
    /// The job's own completion time, if known - lets the last stage's duration be computed too
    /// (see `TimelineRow` below). `nil` while still polling, since the running stage's end isn't
    /// known yet.
    var finishedAt: Date? = nil

    var body: some View {
        if !items.isEmpty {
            let groups = StageGroup.consecutive(from: items)
            SectionCard("Progress") {
                VStack(alignment: .leading, spacing: 0) {
                    // `id: \.offset`, not a group's own startTime: the client truncates the
                    // server's timestamps to millisecond precision, and a stage transition that's
                    // effectively instantaneous (e.g. critic firing right after validation passes)
                    // can land two different groups in the same millisecond - a duplicate `ForEach`
                    // id, which SwiftUI resolves by silently duplicating/dropping rows. `groups` is
                    // append-only across polls, so positional identity is safe here.
                    ForEach(Array(groups.enumerated()), id: \.offset) { index, group in
                        // Messages are sent at stage *start*, so a stage's own duration is the gap
                        // to whatever starts next - the following group, or the job's end time for
                        // the last one - not the gap since the previous stage (that would put the
                        // slow stage's duration on the row after it, not on the stage that was
                        // actually slow).
                        let nextStart = index < groups.count - 1 ? groups[index + 1].startTime : finishedAt
                        TimelineRow(
                            group: group,
                            duration: nextStart.map { $0.timeIntervalSince(group.startTime) },
                            isLast: index == groups.count - 1
                        )
                    }
                }
                .padding(.vertical, 12)
            }
        }
    }
}

/// A run of consecutive `JobFeedback` items sharing the same `stage`.
private struct StageGroup {
    let stage: String
    let items: [JobFeedback]

    var startTime: Date { items[0].timestamp }
    var endTime: Date { items[items.count - 1].timestamp }

    static func consecutive(from items: [JobFeedback]) -> [StageGroup] {
        var groups: [StageGroup] = []
        for item in items {
            if let last = groups.last, last.stage == item.stage {
                groups[groups.count - 1] = StageGroup(stage: last.stage, items: last.items + [item])
            } else {
                groups.append(StageGroup(stage: item.stage, items: [item]))
            }
        }
        return groups
    }
}

/// One row: the rail (dot + connecting line), the stage name, and a sub-line per message the
/// stage reported while it held the rail - usually one, more when it looped (e.g. repeated
/// validation attempts).
private struct TimelineRow: View {
    let group: StageGroup
    /// How long this stage ran for - `nil` for the currently-running stage while still polling,
    /// since there's no "next" timestamp yet to measure against.
    let duration: TimeInterval?
    let isLast: Bool

    /// The stage is still running - the last row while the job is still polling, so there's no
    /// "next" timestamp yet to compute a duration from. Drives the rail's outlined, pulsing dot.
    private var isInProgress: Bool { duration == nil }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            TimelineRail(isLast: isLast, isInProgress: isInProgress)

            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline) {
                    Text(group.stage)
                        .font(.subheadline.weight(.semibold))
                    Spacer()
                    if let duration {
                        Text(durationLabel(duration))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
                ForEach(Array(group.items.enumerated()), id: \.offset) { _, item in
                    messageLine(item)
                }
            }
            .padding(.bottom, isLast ? 0 : 14)
        }
        .padding(.horizontal, 16)
        .accessibilityElement(children: .combine)
    }

    @ViewBuilder
    private func messageLine(_ item: JobFeedback) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Text(item.message)
                .font(.footnote)
                .foregroundStyle(.secondary)
            // Only stages that actually retry (validate/repair/critic) ever report an attempt
            // above 1 - hide the badge otherwise rather than always showing "#1".
            if item.attempt > 1 {
                Text("#\(item.attempt)")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// How long a stage itself ran for.
private func durationLabel(_ interval: TimeInterval) -> String {
    String(format: "%.1fs", max(0, interval))
}

/// The dot-and-line rail down the left edge. The line stretches to fill the row's height (set by
/// the text content beside it) so it meets the next row's dot with no gap.
private struct TimelineRail: View {
    let isLast: Bool
    let isInProgress: Bool

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var isPulsing = false

    var body: some View {
        VStack(spacing: 0) {
            Circle()
                .fill(isInProgress ? Color.clear : Color.accentColor)
                .overlay(Circle().strokeBorder(Color.accentColor, lineWidth: isInProgress ? 1.5 : 0))
                .frame(width: 8, height: 8)
                .scaleEffect(isPulsing ? 1.6 : 1)
                .opacity(isPulsing ? 0.35 : 1)
                // The animation curve itself is picked fresh from `isInProgress` on every change of
                // `isPulsing`, rather than captured once via `withAnimation` - a `repeatForever`
                // committed that way keeps looping even after the row completes, since the loop is
                // attached to `isPulsing` changing, not to `isInProgress` remaining true. Reading
                // `isInProgress` here means the *stopping* transition (pulsing → resting, once a
                // stage completes) plays with a plain one-shot animation instead of inheriting the
                // old repeat.
                .animation(
                    isInProgress && !reduceMotion ? .easeInOut(duration: 0.9).repeatForever(autoreverses: true) : .default,
                    value: isPulsing
                )
                .onChange(of: isInProgress, initial: true) { _, inProgress in
                    isPulsing = inProgress && !reduceMotion
                }
            if !isLast {
                Rectangle()
                    .fill(Color(.separator))
                    .frame(width: 1, height: nil)
                    .frame(maxHeight: .infinity)
            }
        }
        .frame(width: 8)
        .padding(.top, 4)
    }
}

#if DEBUG
#Preview("In progress") {
    ScrollView {
        GenerationTimelineView(items: Array(JobFeedback.previewSequence.prefix(5)))
            .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
}

#Preview("Complete, with a repair loop") {
    ScrollView {
        GenerationTimelineView(
            items: JobFeedback.previewSequence,
            finishedAt: JobFeedback.previewSequence.last!.timestamp.addingTimeInterval(1.8)
        )
        .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
}

#Preview("Complete, with a critic loop") {
    ScrollView {
        GenerationTimelineView(
            items: JobFeedback.previewSequenceWithCriticLoop,
            finishedAt: JobFeedback.previewSequenceWithCriticLoop.last!.timestamp.addingTimeInterval(1.8)
        )
        .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
}

#Preview("Colliding timestamps") {
    ScrollView {
        GenerationTimelineView(
            items: JobFeedback.previewSequenceWithCollidingTimestamps,
            finishedAt: JobFeedback.previewSequenceWithCollidingTimestamps.last!.timestamp.addingTimeInterval(1.8)
        )
        .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
}

#Preview("Empty") {
    ScrollView {
        GenerationTimelineView(items: [])
            .padding(.vertical, 16)
    }
    .background(Color(.systemGroupedBackground))
}
#endif
