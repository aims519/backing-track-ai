import Foundation

/// A single progress message reported by a workflow stage while a job runs. Mirrors the API's
/// `JobFeedbackDto` (`api/src/BackingTrack.Api/Dtos/GenerationJobDto.cs`).
///
/// `attempt` is only meaningful for stages that retry (validate/repair/critic); stages that run
/// once just always report `1`.
struct JobFeedback: Decodable, Hashable, Sendable {
    let timestamp: Date
    let stage: String
    let message: String
    let attempt: Int
}
