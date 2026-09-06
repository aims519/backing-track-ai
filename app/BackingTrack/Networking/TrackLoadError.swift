import Foundation

/// Why `TrackLoader` couldn't produce a playable `CandidateTrack`.
enum TrackLoadError: Error, Equatable {
    /// The download request failed — offline, 4xx/5xx, rejected key. Carries the underlying
    /// `APIClientError` so the screen can reuse its user-facing copy.
    case download(APIClientError)
    /// Bytes arrived but weren't a decodable track document.
    case decode(String)
    /// The job isn't a Precise-mode track (Vibe playback isn't built). Guards a future where a
    /// Vibe job reaches the player screen.
    case notPrecise
}
