import Foundation
import Testing
@testable import BackingTrack

struct JobStatusTests {

    /// Decodes a bare JSON string (as it appears in a `GenerationJobDto.status` field).
    private func decode(_ raw: String) throws -> JobStatus {
        try JSONDecoder().decode(JobStatus.self, from: Data("\"\(raw)\"".utf8))
    }

    @Test(arguments: [
        ("Pending", JobStatus.pending),
        ("InProgress", .inProgress),
        ("Complete", .complete),
        ("CompletedViaFallback", .completedViaFallback),
        ("Failed", .failed),
        ("Cancelled", .cancelled),
    ])
    func decodesEveryBackendState(raw: String, expected: JobStatus) throws {
        #expect(try decode(raw) == expected)
    }

    @Test func unrecognisedStateDecodesToUnknownRatherThanThrowing() throws {
        #expect(try decode("SomethingNew") == .unknown("SomethingNew"))
    }

    @Test(arguments: [
        JobStatus.pending, .inProgress, .complete, .completedViaFallback, .failed, .cancelled,
        .unknown("SomethingNew"),
    ])
    func wireValueRoundTrips(_ status: JobStatus) {
        // The persistence layer stores `wireValue` and rebuilds via `init(wireValue:)`.
        #expect(JobStatus(wireValue: status.wireValue) == status)
    }

    @Test func wireValueMatchesTheBackendStrings() {
        #expect(JobStatus.completedViaFallback.wireValue == "CompletedViaFallback")
        #expect(JobStatus.unknown("Reticulating").wireValue == "Reticulating")
    }

    @Test func terminalAndSuccessFlags() {
        #expect(JobStatus.pending.isTerminal == false)
        #expect(JobStatus.inProgress.isTerminal == false)
        #expect(JobStatus.complete.isTerminal)
        #expect(JobStatus.completedViaFallback.isTerminal)
        #expect(JobStatus.failed.isTerminal)
        #expect(JobStatus.cancelled.isTerminal)

        #expect(JobStatus.complete.isSuccess)
        #expect(JobStatus.completedViaFallback.isSuccess)
        #expect(JobStatus.failed.isSuccess == false)
        #expect(JobStatus.cancelled.isSuccess == false)
        #expect(JobStatus.unknown("x").isSuccess == false)
    }
}
