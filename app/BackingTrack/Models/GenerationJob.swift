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
