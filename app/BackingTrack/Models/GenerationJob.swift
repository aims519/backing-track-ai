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
    let timeSignatureBeatCount: Int
    let timeSignatureBeatUnit: Int
}

// `id` already satisfies the requirement — this just lets a job drive `.fullScreenCover(item:)`
// and other identity-keyed SwiftUI APIs on the progress screen.
extension GenerationJob: Identifiable {}

#if DEBUG
extension GenerationJob {
    /// A representative job for `#Preview`s and tests — `endTime`/`trackId` are filled in to match
    /// `status` so a `.complete` fixture looks like a real completed job.
    static func preview(id: UUID = UUID(), status: JobStatus = .inProgress) -> GenerationJob {
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
            timeSignatureBeatCount: 4,
            timeSignatureBeatUnit: 4
        )
    }
}
#endif
