import Foundation
import SwiftData
import Testing
@testable import BackingTrack

/// Exercises `SwiftDataJobHistoryStore` against a real (in-memory) SwiftData stack — `init()`
/// gives every case a fresh, isolated store.
///
/// `.serialized` because spinning up several `ModelContainer`s for the same `@Model` type
/// concurrently — which the default parallelism would do — trips SwiftData's global model cache
/// and traps.
@MainActor
@Suite(.serialized)
struct JobHistoryStoreTests {

    private let store: SwiftDataJobHistoryStore

    init() throws {
        let container = try ModelContainer(
            for: JobHistoryEntry.self,
            configurations: ModelConfiguration(isStoredInMemoryOnly: true)
        )
        store = SwiftDataJobHistoryStore(container: container)
    }

    @Test func upsertInsertsOnFirstSubmit() throws {
        let job = GenerationJob.preview(status: .pending)

        try store.upsertOnSubmit(job: job, style: "Blues - brushes not sticks")

        let entry = try #require(try store.entry(jobID: job.id))
        #expect(entry.jobID == job.id)
        #expect(entry.style == "Blues - brushes not sticks")
        #expect(entry.status == .pending)
        #expect(entry.disposition == .inFlight)
        #expect(entry.chordSymbols == job.chords)
        #expect(entry.tempo == job.tempo)
    }

    @Test func upsertIsIdempotentAndPreservesCreatedAt() throws {
        let job = GenerationJob.preview(status: .pending)
        try store.upsertOnSubmit(job: job, style: "Rock")
        let createdAt = try #require(try store.entry(jobID: job.id)).createdAt

        try store.upsertOnSubmit(job: job, style: "Rock")

        let all = try store.allEntries()
        #expect(all.count == 1)
        #expect(all[0].createdAt == createdAt)
    }

    @Test func recordStatusUpdatesLiveStatus() throws {
        let job = GenerationJob.preview(status: .pending)
        try store.upsertOnSubmit(job: job, style: "Pop")

        try store.recordStatus(jobID: job.id, status: .inProgress, startedAt: nil)

        #expect(try #require(try store.entry(jobID: job.id)).status == .inProgress)
    }

    @Test func recordStatusIsANoOpForAnUnknownJob() throws {
        // The row was cleared out from under the poll loop — must not throw or resurrect it.
        try store.recordStatus(jobID: UUID(), status: .inProgress, startedAt: nil)
        #expect(try store.allEntries().isEmpty)
    }

    @Test func recordDispositionIsANoOpForAnUnknownJob() throws {
        try store.recordDisposition(jobID: UUID(), .lost, job: nil, error: "gone")
        #expect(try store.allEntries().isEmpty)
    }

    @Test func entryReturnsTheRightRowAmongManyAndNilForUnknown() throws {
        let a = GenerationJob.preview(id: UUID(), status: .pending)
        let b = GenerationJob.preview(id: UUID(), status: .pending)
        let c = GenerationJob.preview(id: UUID(), status: .pending)
        try store.upsertOnSubmit(job: a, style: "A")
        try store.upsertOnSubmit(job: b, style: "B")
        try store.upsertOnSubmit(job: c, style: "C")

        #expect(try store.entry(jobID: b.id)?.style == "B")
        #expect(try store.entry(jobID: UUID()) == nil)
    }

    @Test func startedAtIsCapturedFromTheTerminalPollWhenSubmitLackedIt() throws {
        let id = UUID()
        let submitted = GenerationJob(
            id: id, status: .pending, startTime: nil, endTime: nil, trackId: nil,
            audioLocation: nil, mode: "Precise", tempo: 120, chords: ["C"], keyRoot: "C",
            keyMode: "Ionian", keyModeWarnings: [],
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, feedbackItems: []
        )
        try store.upsertOnSubmit(job: submitted, style: "Rock")
        let done = GenerationJob.preview(id: id, status: .complete)  // fixture sets startTime

        try store.recordDisposition(jobID: id, .ready, job: done, error: nil)

        let entry = try #require(try store.entry(jobID: id))
        #expect(entry.startedAt == done.startTime)
        #expect(entry.finishedAt == done.endTime)
    }

    @Test func readyDispositionFoldsInTrackAndFinishTime() throws {
        let job = GenerationJob.preview(status: .pending)
        try store.upsertOnSubmit(job: job, style: "Funk")
        let completed = GenerationJob.preview(id: job.id, status: .complete)

        try store.recordDisposition(jobID: job.id, .ready, job: completed, error: nil)

        let entry = try #require(try store.entry(jobID: job.id))
        #expect(entry.disposition == .ready)
        #expect(entry.status == .complete)
        #expect(entry.trackID == completed.trackId)
        #expect(entry.finishedAt == completed.endTime)
    }

    @Test(arguments: [JobDisposition.timedOut, .lost, .serverFailed, .pollError, .cancelled])
    func terminalDispositionsRoundTrip(_ disposition: JobDisposition) throws {
        let job = GenerationJob.preview(status: .inProgress)
        try store.upsertOnSubmit(job: job, style: "Jazz")

        try store.recordDisposition(jobID: job.id, disposition, job: nil, error: "some detail")

        let entry = try #require(try store.entry(jobID: job.id))
        #expect(entry.disposition == disposition)
        #expect(entry.lastClientError == "some detail")
    }

    @Test func unknownStatusRoundTripsItsRawString() throws {
        let job = GenerationJob.preview(status: .pending)
        try store.upsertOnSubmit(job: job, style: "Blues")

        try store.recordStatus(jobID: job.id, status: .unknown("Reticulating"), startedAt: nil)

        #expect(try #require(try store.entry(jobID: job.id)).status == .unknown("Reticulating"))
    }

    @Test func cachePreciseContentStoresBytesKindAndTimestamp() throws {
        let job = GenerationJob.preview(status: .complete)
        try store.upsertOnSubmit(job: job, style: "Blues")
        let bytes = Data("{\"LengthInBeats\":8,\"Drums\":[],\"Bass\":[],\"ChordVoicings\":[]}".utf8)
        let cachedAt = Date(timeIntervalSince1970: 1_800_000_000)

        try store.cachePreciseContent(jobID: job.id, data: bytes, cachedAt: cachedAt)

        let entry = try #require(try store.entry(jobID: job.id))
        #expect(entry.preciseContentData == bytes)
        #expect(entry.contentKindValue == .preciseMidiJson)
        #expect(entry.contentCachedAt == cachedAt)
    }

    @Test func cachePreciseContentIsANoOpForAnUnknownJob() throws {
        try store.cachePreciseContent(jobID: UUID(), data: Data("x".utf8), cachedAt: .now)
        #expect(try store.allEntries().isEmpty)
    }

    @Test func cachePreciseContentOverwritesAnEarlierCache() throws {
        let job = GenerationJob.preview(status: .complete)
        try store.upsertOnSubmit(job: job, style: "Rock")
        try store.cachePreciseContent(jobID: job.id, data: Data("first".utf8), cachedAt: .now)

        let second = Data("second".utf8)
        try store.cachePreciseContent(jobID: job.id, data: second, cachedAt: .now)

        #expect(try #require(try store.entry(jobID: job.id)).preciseContentData == second)
    }

    @Test func deleteRemovesTheRow() throws {
        let job = GenerationJob.preview(status: .complete)
        try store.upsertOnSubmit(job: job, style: "Reggae")

        try store.delete(jobID: job.id)

        #expect(try store.entry(jobID: job.id) == nil)
        #expect(try store.allEntries().isEmpty)
    }

    @Test func deleteIsANoOpForAnUnknownJob() throws {
        let job = GenerationJob.preview(status: .complete)
        try store.upsertOnSubmit(job: job, style: "Reggae")

        try store.delete(jobID: UUID())

        #expect(try store.allEntries().count == 1)
    }

    @Test func allEntriesAreNewestFirst() async throws {
        let older = GenerationJob.preview(id: UUID(), status: .pending)
        let newer = GenerationJob.preview(id: UUID(), status: .pending)
        try store.upsertOnSubmit(job: older, style: "A")
        try await Task.sleep(for: .milliseconds(5)) // keep the `createdAt` timestamps apart
        try store.upsertOnSubmit(job: newer, style: "B")

        let all = try store.allEntries()

        #expect(all.map(\.style) == ["B", "A"])
    }
}
