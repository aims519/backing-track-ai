import Foundation

/// How `GenerationProgressModel` polls `GET /tracks/status/{jobId}`: the cadence between polls,
/// how long to keep polling before declaring the job stuck, and how many consecutive failed
/// polls to ride out before giving up.
///
/// `interval` follows the same plist-key pattern as `APIConfiguration.baseURL`: it comes from the
/// `StatusPollInterval` Info.plist key, fed from the `STATUS_POLL_INTERVAL` build setting (see
/// `app/project.yml`). Unlike `APIConfiguration`, a missing or malformed value is *not* fatal —
/// it falls back to a sensible default rather than `preconditionFailure`, because a wrong poll
/// cadence only degrades the progress screen, it doesn't break the app.
///
/// `timeout` and `maxConsecutiveFailures` are client-resilience policy, not per-machine
/// environment knobs, so they're plain defaults here. Tests construct their own instance with
/// millisecond durations.
struct PollingConfiguration: Sendable {
    /// Delay between successive status polls.
    var interval: Duration
    /// Total time to keep polling a non-terminal job before switching to the timed-out state.
    var timeout: Duration
    /// How many *consecutive* failed polls to tolerate before failing the screen. A single good
    /// poll resets the count, so a flaky connection that recovers never trips this.
    var maxConsecutiveFailures: Int

    /// The configuration used in the running app.
    ///
    /// `timeout` of 180s sits well above Precise mode's worst case (~25s, see ADR 0013) with
    /// headroom for Vibe mode when it lands. `maxConsecutiveFailures` of 4 at a 2s cadence is
    /// ~8s of sustained failure before giving up — long enough to ride out a brief network drop,
    /// short enough that a genuinely dead API surfaces quickly.
    static let live = PollingConfiguration(
        interval: .seconds(
            intervalSeconds(
                fromPlistValue: Bundle.main.object(forInfoDictionaryKey: "StatusPollInterval") as? String
            )
        ),
        timeout: .seconds(180),
        maxConsecutiveFailures: 4
    )

    /// Parses the `StatusPollInterval` plist string into a positive number of seconds, falling
    /// back to `2` for a missing, blank, non-numeric, or non-positive value. Pure, so it can be
    /// tested without touching `Bundle.main`.
    static func intervalSeconds(fromPlistValue raw: String?) -> Double {
        guard let raw,
              let value = Double(raw.trimmingCharacters(in: .whitespaces)),
              value > 0
        else { return 2 }
        return value
    }
}
