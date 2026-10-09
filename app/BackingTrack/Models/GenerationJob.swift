import Foundation

/// A generation job as returned by `POST /tracks/generate` and `GET /tracks/status/{jobId}`.
/// Mirrors the API's `GenerationJobDto` (`api/src/BackingTrack.Api/Dtos/GenerationJobDto.cs`).
///
/// The wire format is camelCase (ASP.NET's web defaults — confirmed by the checked-in
/// `api/src/BackingTrack.Api/BackingTrack.Api.http` requests reading `response.body.id`), so the
/// property names match the JSON directly and no `CodingKeys` are needed. This is the opposite of
/// `GenerationRequest`, which needs explicit PascalCase keys for the request body.
struct GenerationJob: Decodable, Hashable, Sendable {
    let id: UUID
    let status: JobStatus
    /// Optional to match the DTO (`GenerationJobDto.StartTime` is `DateTimeOffset?`), even though
    /// the endpoint currently always sets it.
    let startTime: Date?
    let endTime: Date?
    /// Set once the job produces a track; the id to pass to `GET /tracks/download/{trackId}`.
    let trackId: UUID?
    let audioLocation: String?

    /// Echoed-back request parameters. Useful for a "generating…" screen that shows what was asked
    /// for without the app having to hold onto the original request.
    let mode: String
    let tempo: Int
    let chords: [String]
    let keyRoot: String
    let keyMode: String
    /// Chords whose own notes fall outside the declared key/mode — advisory only, generation
    /// still proceeds (`ScaleMatcher.GetChordsOutsideKey`). Empty when everything fits.
    let keyModeWarnings: [String]
    let timeSignatureBeatCount: Int
    let timeSignatureBeatUnit: Int
    /// Chronological progress messages from the workflow stages the job has passed through so
    /// far. Grows across polls; never shrinks or reorders.
    let feedbackItems: [JobFeedback]
}

// `id` already satisfies the requirement — this just lets a job drive `.fullScreenCover(item:)`
// and other identity-keyed SwiftUI APIs on the progress screen.
extension GenerationJob: Identifiable {}

#if DEBUG
extension GenerationJob {
    /// A representative job for `#Preview`s and tests — `endTime`/`trackId` are filled in to match
    /// `status` so a `.complete` fixture looks like a real completed job.
    static func preview(
        id: UUID = UUID(),
        status: JobStatus = .inProgress,
        keyMode: String = "Ionian",
        feedbackItems: [JobFeedback] = []
    ) -> GenerationJob {
        GenerationJob(
            id: id,
            status: status,
            startTime: Date(timeIntervalSinceNow: -42),
            endTime: status.isTerminal ? Date() : nil,
            trackId: status.isSuccess ? UUID() : nil,
            audioLocation: nil,
            mode: "Precise",
            tempo: 120,
            chords: ["C", "Amin", "F", "G"],
            keyRoot: "C",
            keyMode: keyMode,
            keyModeWarnings: [],
            timeSignatureBeatCount: 4,
            timeSignatureBeatUnit: 4,
            feedbackItems: feedbackItems
        )
    }
}

extension JobFeedback {
    /// A representative run through the pipeline, including one repair/validate loop, for
    /// `#Preview`s of the progress timeline.
    static let previewSequence: [JobFeedback] = {
        let start = Date(timeIntervalSinceNow: -38)
        return [
            JobFeedback(timestamp: start, stage: "Gathering Additional Information", message: "Fetching musical style details...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(1.1), stage: "Gathering Additional Information", message: "Fetching chord details...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(1.8), stage: "Gathering Additional Information", message: "Fetching matching scales for track chords...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(2.4), stage: "Generating Track", message: "Generating 4-bar backing track in C Ionian...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.7), stage: "Checking Track", message: "Checking generated track for errors...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.8), stage: "Checking Track", message: "Found 1 issue in the track", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.9), stage: "Applying Repairs", message: "Fixing 2 track issues...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(11.3), stage: "Checking Track", message: "Checking generated track for errors...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(11.4), stage: "Checking Track", message: "No problems found!", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(11.5), stage: "Quality Control", message: "Ensuring musical quality of generated track...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(11.7), stage: "Quality Control", message: "Track passed with score 91%", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(15.2), stage: "Saving", message: "Saving your track for playback...", attempt: 1)
        ]
    }()

    /// A run where the critic rejects the first candidate, triggering a full regenerate/validate
    /// pass before it passes on the second review - `attempt` tracks `CriticAttempts`, mirroring
    /// how `CriticExecutor`/`GenerateCandidateTrackExecutor` report it.
    static let previewSequenceWithCriticLoop: [JobFeedback] = {
        let start = Date(timeIntervalSinceNow: -44)
        return [
            JobFeedback(timestamp: start, stage: "Gathering Additional Information", message: "Fetching musical style details...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(1.1), stage: "Gathering Additional Information", message: "Fetching chord details...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(1.8), stage: "Gathering Additional Information", message: "Fetching matching scales for track chords...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(2.4), stage: "Generating Track", message: "Generating 4-bar backing track in C Ionian...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.7), stage: "Checking Track", message: "Checking generated track for errors...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.8), stage: "Checking Track", message: "No problems found!", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(9.9), stage: "Quality Control", message: "Ensuring musical quality of generated track...", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(10.1), stage: "Quality Control", message: "Track failed with score 41%\n- Voice leading failed\n- Rhythm and feel failed", attempt: 1),
            JobFeedback(timestamp: start.addingTimeInterval(13.6), stage: "Generating Track", message: "Generating 4-bar backing track in C Ionian...", attempt: 2),
            JobFeedback(timestamp: start.addingTimeInterval(20.4), stage: "Checking Track", message: "Checking generated track for errors...", attempt: 2),
            JobFeedback(timestamp: start.addingTimeInterval(20.5), stage: "Checking Track", message: "No problems found!", attempt: 2),
            JobFeedback(timestamp: start.addingTimeInterval(20.6), stage: "Quality Control", message: "Ensuring musical quality of generated track...", attempt: 2),
            JobFeedback(timestamp: start.addingTimeInterval(20.8), stage: "Quality Control", message: "Track passed with score 88%", attempt: 2),
            JobFeedback(timestamp: start.addingTimeInterval(24.3), stage: "Saving", message: "Saving your track for playback...", attempt: 1)
        ]
    }()

    /// Regression fixture for a `GenerationTimelineView` bug: the client's date decoder trims the
    /// server's timestamps to millisecond precision, so a stage transition that's effectively
    /// instantaneous - a passed validation immediately followed by the critic's start message - can
    /// land two different groups in the *same* millisecond. `Validation` and `Critic Review` below
    /// share one down to the millisecond, on purpose, to prove `GenerationTimelineView` still shows
    /// both as distinct rows rather than duplicating one and dropping the other.
    static let previewSequenceWithCollidingTimestamps: [JobFeedback] = {
        // All three land at the identical Date value on purpose - reproducing what the client's
        // millisecond-truncating date decoder actually produces for messages this close together.
        let collision = Date(timeIntervalSinceNow: -10)
        return [
            JobFeedback(timestamp: collision.addingTimeInterval(-9.7), stage: "Generating Track", message: "Generating 4-bar backing track in A Aeolian...", attempt: 1),
            JobFeedback(timestamp: collision, stage: "Checking Track", message: "Checking generated track for errors...", attempt: 1),
            JobFeedback(timestamp: collision, stage: "Checking Track", message: "No problems found!", attempt: 1),
            JobFeedback(timestamp: collision, stage: "Quality Control", message: "Ensuring musical quality of generated track...", attempt: 1),
            JobFeedback(timestamp: collision.addingTimeInterval(7.3), stage: "Quality Control", message: "Track passed with score 100%", attempt: 1),
            JobFeedback(timestamp: collision.addingTimeInterval(7.5), stage: "Saving", message: "Saving your track for playback...", attempt: 1)
        ]
    }()
}
#endif
