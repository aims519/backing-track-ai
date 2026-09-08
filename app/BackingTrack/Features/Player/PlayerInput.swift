import Foundation

/// Everything the player screen needs that the downloaded `CandidateTrack` doesn't carry:
/// identity, and the musical context (tempo, key, time signature, chord-per-bar labels).
///
/// Rides the navigation route, so `Hashable`. Both entry points — a job just finished on the
/// progress screen, a stored history row — already hold every field, so building one needs no
/// extra fetch.
struct PlayerInput: Hashable {
    var jobID: UUID
    var trackID: UUID
    var tempo: Int
    var keyRoot: String
    var chordSymbols: [String]
    var timeSignature: TimeSignature
    /// The job's generation mode, as the wire string. Vibe playback isn't built — `load()` fails
    /// fast on a non-Precise input rather than trying to decode audio as note events.
    var mode: String
    var title: String?

    var isPrecise: Bool {
        mode.caseInsensitiveCompare(GenerationMode.precise.apiValue) == .orderedSame
    }
}

extension PlayerInput {
    /// From a job that just reached a ready state on the progress screen. `nil` until the job has
    /// a track id.
    static func from(job: GenerationJob, style: String?) -> PlayerInput? {
        guard let trackID = job.trackId else { return nil }
        return PlayerInput(
            jobID: job.id,
            trackID: trackID,
            tempo: job.tempo,
            keyRoot: job.keyRoot,
            chordSymbols: job.chords,
            timeSignature: TimeSignature(
                beatCount: job.timeSignatureBeatCount,
                beatUnit: job.timeSignatureBeatUnit
            ),
            mode: job.mode,
            title: style
        )
    }

    /// From a stored history row. `nil` if the row never produced a track (or somehow has no job
    /// id — `jobIDString` is only ever written from a real `UUID`).
    static func from(entry: JobHistoryEntry) -> PlayerInput? {
        guard let jobID = entry.jobID, let trackID = entry.trackID else { return nil }
        return PlayerInput(
            jobID: jobID,
            trackID: trackID,
            tempo: entry.tempo,
            keyRoot: entry.keyRoot,
            chordSymbols: entry.chordSymbols,
            timeSignature: TimeSignature(
                beatCount: entry.timeSignatureBeatCount,
                beatUnit: entry.timeSignatureBeatUnit
            ),
            mode: entry.mode,
            title: entry.style
        )
    }
}

/// Navigation route to the player screen. Mirrors `HistoryRoute` living beside its screen.
enum PlayerRoute: Hashable {
    case player(PlayerInput)
}

#if DEBUG
extension PlayerInput {
    static func preview(
        mode: String = "Precise",
        chordSymbols: [String] = ["C", "Amin", "F", "G7"],
        tempo: Int = 120
    ) -> PlayerInput {
        PlayerInput(
            jobID: UUID(),
            trackID: UUID(),
            tempo: tempo,
            keyRoot: "C",
            chordSymbols: chordSymbols,
            timeSignature: .fourFour,
            mode: mode,
            title: "Blues — slow, brushes"
        )
    }
}
#endif
