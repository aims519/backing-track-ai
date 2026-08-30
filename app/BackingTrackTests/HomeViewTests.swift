// `Testing` is Apple's newer test framework (Xcode 16+), the SwiftUI-era counterpart to
// XCTest — attribute-based (`@Test`, like xunit's `[Fact]` on the API side) rather than
// XCTest's "method name must start with test" convention, with plain `#expect` assertions
// instead of a large family of `XCTAssert*` calls.
import Testing
@testable import BackingTrack

struct HomeViewTests {
    // Scaffold placeholder — there's no real logic to test yet. Once generation/networking
    // code lands, this file should be split up (e.g. one test file per type under test,
    // mirroring the api project's convention) rather than growing as a single catch-all.
    @Test func placeholderScaffoldBuilds() {
        #expect(1 + 1 == 2)
    }
}
