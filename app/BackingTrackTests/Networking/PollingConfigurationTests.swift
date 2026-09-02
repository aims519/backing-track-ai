import Testing
@testable import BackingTrack

/// Covers the one part of `PollingConfiguration` that can silently break: parsing the
/// `StatusPollInterval` plist string. `PollingConfiguration.live` itself reads `Bundle.main`, so
/// it isn't unit-tested here.
struct PollingConfigurationTests {

    @Test(arguments: zip(
        ["1", "2", "1.5", "  3  ", "0.25"],
        [1.0, 2.0, 1.5, 3.0, 0.25]
    ))
    func parsesPositiveNumbers(_ raw: String, _ expected: Double) {
        #expect(PollingConfiguration.intervalSeconds(fromPlistValue: raw) == expected)
    }

    @Test(arguments: [
        nil,                        // key absent
        "",                         // key present but empty
        "  ",                       // whitespace only
        "garbage",                  // non-numeric
        "$(STATUS_POLL_INTERVAL)",  // build setting never substituted
        "0",                        // not positive
        "-1",                       // negative
    ] as [String?])
    func fallsBackToTwoForUnusableValues(_ raw: String?) {
        #expect(PollingConfiguration.intervalSeconds(fromPlistValue: raw) == 2)
    }
}
