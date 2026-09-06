import Foundation

/// Gets a completed job's note-event track ready to play: cache first, download second, decode,
/// and cache what it downloaded.
///
/// The player-screen counterpart to `JobReconciler` — a one-shot, `Result`-returning `@MainActor`
/// helper that never throws and swallows store-write failures (ADR 0024). ADR 0025 anticipated
/// this as "the player screen's download-and-cache pass"; it's deliberately its own type, not the
/// deferred `JobTracker` extraction, for the same reasons 0025 gives.
@MainActor
struct TrackLoader {
    var apiClient: any BackingTrackAPIClient
    var historyStore: any JobHistoryStore

    /// Returns the track for `trackID`, preferring the copy cached on the job's history row.
    /// `jobID` is the history key; `trackID` is the download key.
    func load(trackID: UUID, jobID: UUID) async -> Result<CandidateTrack, TrackLoadError> {
        if let cached = cachedData(jobID: jobID) {
            switch decode(cached) {
            case .success(let track):
                return .success(track)
            case .failure:
                // A corrupt cache row shouldn't strand the user offline-forever — fall through to
                // a fresh download, which overwrites it on success.
                break
            }
        }

        let data: Data
        do {
            data = try await apiClient.downloadTrackData(trackID: trackID)
        } catch let error as APIClientError {
            return .failure(.download(error))
        } catch {
            return .failure(.download(.decoding(String(describing: error))))
        }

        switch decode(data) {
        case .success(let track):
            cache(data, jobID: jobID)
            return .success(track)
        case .failure(let error):
            return .failure(error)
        }
    }

    private func cachedData(jobID: UUID) -> Data? {
        guard let entry = try? historyStore.entry(jobID: jobID),
              entry.contentKindValue == .preciseMidiJson
        else { return nil }
        return entry.preciseContentData
    }

    private func decode(_ data: Data) -> Result<CandidateTrack, TrackLoadError> {
        do {
            return .success(try CandidateTrack.decode(from: data))
        } catch {
            return .failure(.decode(String(describing: error)))
        }
    }

    private func cache(_ data: Data, jobID: UUID) {
        // A cache-write failure never fails the load — the track is already in hand (ADR 0024).
        try? historyStore.cachePreciseContent(jobID: jobID, data: data, cachedAt: .now)
    }
}
