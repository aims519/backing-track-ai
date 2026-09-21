import Foundation

/// The lifecycle state of a generation job, mirroring the backend's `JobStates` enum
/// (`api/src/BackingTrack.Generation/Jobs/JobStates.cs`). The API returns it as the string form
/// of that enum in `GenerationJobDto.Status` (e.g. `"Pending"`, `"InProgress"`).
///
/// The full submitted → generating → ready/failed state machine that drives the UI is a separate
/// task (see the "Sketch iOS-side state machine" Notion task); this type only needs to carry the
/// value off the wire and answer the two questions the submit flow asks of it.
enum JobStatus: Sendable, Hashable {
    case pending
    case inProgress
    case complete
    case completedViaFallback
    case failed
    /// The user cancelled the job (`POST /tracks/cancel/{jobId}`).
    case cancelled
    /// A status string the app doesn't recognise — keeps decoding total if the backend adds a
    /// state before the app is updated, rather than throwing.
    case unknown(String)

    var displayName: String {
        switch self {
        case .pending: "Pending"
        case .inProgress: "In progress"
        case .complete: "Complete"
        case .completedViaFallback: "Complete (fallback)"
        case .failed: "Failed"
        case .cancelled: "Cancelled"
        case .unknown(let raw): raw
        }
    }

    /// Whether the job has reached an end state (won't change on further polling).
    var isTerminal: Bool {
        switch self {
        case .complete, .completedViaFallback, .failed, .cancelled: true
        case .pending, .inProgress, .unknown: false
        }
    }

    /// Whether the job finished with a usable track. `completedViaFallback` counts — the track is
    /// real, it just came from the rule-based fallback rather than the model (the UI flags that
    /// difference with a one-time alert; that's the separate state-machine task).
    var isSuccess: Bool {
        switch self {
        case .complete, .completedViaFallback: true
        case .pending, .inProgress, .failed, .cancelled, .unknown: false
        }
    }
}

extension JobStatus {
    /// The backend's `JobStates` enum name, as it appears on the wire. The exact inverse of
    /// `init(wireValue:)`: `.unknown` round-trips the string it captured, so a state the app
    /// doesn't recognise still persists and re-decodes losslessly.
    var wireValue: String {
        switch self {
        case .pending: "Pending"
        case .inProgress: "InProgress"
        case .complete: "Complete"
        case .completedViaFallback: "CompletedViaFallback"
        case .failed: "Failed"
        case .cancelled: "Cancelled"
        case .unknown(let raw): raw
        }
    }

    /// Maps a backend status string onto a case, keeping an unrecognised value as `.unknown(raw)`
    /// rather than failing. Shared by the `Decodable` conformance and the persistence layer, which
    /// stores this string directly (see `JobHistoryEntry`).
    init(wireValue raw: String) {
        self = switch raw {
        case "Pending": .pending
        case "InProgress": .inProgress
        case "Complete": .complete
        case "CompletedViaFallback": .completedViaFallback
        case "Failed": .failed
        case "Cancelled": .cancelled
        default: .unknown(raw)
        }
    }
}

extension JobStatus: Decodable {
    init(from decoder: any Decoder) throws {
        self.init(wireValue: try decoder.singleValueContainer().decode(String.self))
    }
}
