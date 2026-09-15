import Foundation

extension GenerationJob {
    /// Rebuilds a wire `GenerationJob` from a stored history row — enough to seed
    /// `GenerationProgressViewModel` (and the summary card it renders) when the user taps
    /// "Watch progress" on a job that's still running. `nil` if the row's id didn't round-trip.
    ///
    /// `audioLocation` is always `nil` — the DTO field is never populated server-side, and the
    /// history row doesn't keep it.
    static func seed(from entry: JobHistoryEntry) -> GenerationJob? {
        guard let id = entry.jobID else { return nil }
        return GenerationJob(
            id: id,
            status: entry.status,
            startTime: entry.startedAt,
            endTime: entry.finishedAt,
            trackId: entry.trackID,
            audioLocation: nil,
            mode: entry.mode,
            tempo: entry.tempo,
            chords: entry.chordSymbols,
            keyRoot: entry.keyRoot,
            keyMode: entry.keyMode,
            keyModeWarnings: [],
            timeSignatureBeatCount: entry.timeSignatureBeatCount,
            timeSignatureBeatUnit: entry.timeSignatureBeatUnit
        )
    }
}
