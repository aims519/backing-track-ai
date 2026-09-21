import Foundation

/// What the *client* ended up doing with a generation job — the durable, flattened form of
/// `GenerationProgressViewModel.Phase` + `.Failure`.
///
/// `Phase`/`Failure` carry payloads (the job, an `APIClientError`) that don't belong in a stored
/// record, and distinctions that stop mattering once the screen is gone. This keeps only what the
/// history list needs, as a single string.
///
/// The key cases are `.timedOut` / `.lost`: ADR 0023 notes that a job the client stops following
/// (poll budget spent, or the in-memory job dropped by an API restart) may still finish
/// server-side "with nowhere for the user to see it". Persisting the disposition is what gives it
/// somewhere.
enum JobDisposition: Equatable, Sendable {
    /// Submitted and being polled — no terminal outcome yet.
    case inFlight
    /// `Complete` or `CompletedViaFallback` — a usable track exists.
    case ready
    /// The server reported the job as `Failed`.
    case serverFailed
    /// The client's poll budget was spent before a terminal state; the server job may still finish.
    case timedOut
    /// A sustained `404` — an API restart dropped the in-memory job (ADR 0013).
    case lost
    /// The client gave up after repeated poll errors, or an error that wouldn't self-heal.
    case pollError
    /// The user cancelled the job (`POST /tracks/cancel/{jobId}`).
    case cancelled

    var rawValue: String {
        switch self {
        case .inFlight: "inFlight"
        case .ready: "ready"
        case .serverFailed: "serverFailed"
        case .timedOut: "timedOut"
        case .lost: "lost"
        case .pollError: "pollError"
        case .cancelled: "cancelled"
        }
    }

    /// An unrecognised stored value (an older/newer build wrote it) reads back as `.inFlight` —
    /// the safe default, since it's the only non-terminal case and a re-poll can resolve it.
    init(rawValue raw: String) {
        self = switch raw {
        case "ready": .ready
        case "serverFailed": .serverFailed
        case "timedOut": .timedOut
        case "lost": .lost
        case "pollError": .pollError
        case "cancelled": .cancelled
        default: .inFlight
        }
    }
}
