import Foundation
import Testing
@testable import BackingTrack

@Suite("In-flight summary")
struct InFlightSummaryTests {
    private let now = Date(timeIntervalSince1970: 1_700_000_000)

    private func running(minutesAgo: Double) -> JobHistoryEntry {
        .preview(
            createdAt: now.addingTimeInterval(-minutesAgo * 60),
            status: .inProgress,
            disposition: .inFlight
        )
    }

    @Test func nothingInFlightGivesNoSummary() {
        let entries = [
            JobHistoryEntry.preview(disposition: .ready),
            JobHistoryEntry.preview(disposition: .timedOut),
            JobHistoryEntry.preview(disposition: .serverFailed),
        ]

        #expect(InFlightSummary(entries: entries) == nil)
        #expect(InFlightSummary(entries: []) == nil)
    }

    @Test func aSingleRunningJobHasNoOthers() throws {
        let job = running(minutesAgo: 1)

        let summary = try #require(InFlightSummary(entries: [job]))

        #expect(summary.headline === job)
        #expect(summary.othersCount == 0)
    }

    @Test func theOldestRunningJobLeadsRegardlessOfInputOrder() throws {
        let oldest = running(minutesAgo: 30)
        let newest = running(minutesAgo: 1)
        let middle = running(minutesAgo: 10)

        let summary = try #require(InFlightSummary(entries: [newest, oldest, middle]))

        #expect(summary.headline === oldest)
        #expect(summary.othersCount == 2)
    }

    @Test func aJobQueuedBehindTheActiveOneDoesNotTakeOverTheBanner() throws {
        // The API runs jobs one at a time in submission order: the older job is generating, the
        // one just submitted is still pending behind it.
        let generating = JobHistoryEntry.preview(
            createdAt: now.addingTimeInterval(-120), status: .inProgress, disposition: .inFlight
        )
        let queued = JobHistoryEntry.preview(createdAt: now, status: .pending, disposition: .inFlight)

        let summary = try #require(InFlightSummary(entries: [generating, queued]))

        #expect(summary.headline === generating)
        #expect(summary.othersCount == 1)
    }

    @Test func theNextOldestTakesOverOnceTheLeadJobFinishes() throws {
        let first = running(minutesAgo: 5)
        let second = running(minutesAgo: 2)
        #expect(try #require(InFlightSummary(entries: [first, second])).headline === first)

        first.disposition = .ready

        let summary = try #require(InFlightSummary(entries: [first, second]))
        #expect(summary.headline === second)
        #expect(summary.othersCount == 0)
    }

    @Test func rowsThatAreNotInFlightAreIgnored() throws {
        // A newer finished job must neither lead nor be counted among the others.
        let finishedJustNow = JobHistoryEntry.preview(createdAt: now, disposition: .ready)
        let timedOut = JobHistoryEntry.preview(createdAt: now, disposition: .timedOut)
        let job = running(minutesAgo: 5)

        let summary = try #require(InFlightSummary(entries: [finishedJustNow, timedOut, job]))

        #expect(summary.headline === job)
        #expect(summary.othersCount == 0)
    }
}
