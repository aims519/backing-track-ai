import Foundation

/// A single progress message reported by a workflow stage while a job runs. Mirrors the API's
/// `JobFeedbackDto` (`api/src/BackingTrack.Api/Dtos/GenerationJobDto.cs`).
///
/// `attempt` is the generation round: every stage in a round reports the same number, and it only
/// goes above `1` when the critic rejected a track and the workflow generated again.
struct JobFeedback: Decodable, Hashable, Sendable {
    let timestamp: Date
    let stage: String
    let message: String
    let attempt: Int
}
