import Foundation

/// What the dashboard banner says about the jobs currently generating: the one to lead with, and
/// how many others are waiting behind it. Pure, so the choice can be tested without a view.
struct InFlightSummary {
    /// The *oldest* running job. The API works through jobs one at a time, in the order they were
    /// submitted (a single worker draining a channel), so the oldest unfinished job is the one
    /// actually generating and anything newer is queued behind it. Leading with the newest would
    /// point the banner at a job that hasn't started. When the lead job finishes, the next-oldest
    /// takes over.
    let headline: JobHistoryEntry
    let othersCount: Int

    /// `nil` when nothing is in flight. Re-reads each row's disposition rather than trusting the
    /// caller's filter, and doesn't depend on the order `entries` arrives in.
    init?(entries: [JobHistoryEntry]) {
        let inFlight = entries.filter { $0.disposition == .inFlight }
        guard let oldest = inFlight.min(by: { $0.createdAt < $1.createdAt }) else { return nil }
        headline = oldest
        othersCount = inFlight.count - 1
    }
}
