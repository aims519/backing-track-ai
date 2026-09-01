import Foundation

/// The two generation modes from the project scope (`docs/backing-track-generator-scope.md`).
///
/// Only `.precise` is wired to the API today — `.vibe` (a third-party generative-audio service)
/// isn't built yet, so `isAvailable` is `false` for it and the UI shows it disabled rather than
/// hiding it, to signal it's coming.
enum GenerationMode: String, CaseIterable, Identifiable, Sendable {
    case precise
    case vibe

    // `Identifiable` conformance — `ForEach` and `Picker` use this to tell rows apart. The raw
    // string value is already unique, so it doubles as the id.
    var id: String { rawValue }

    /// Label shown in the mode selector.
    var displayName: String {
        switch self {
        case .precise: "Precise"
        case .vibe: "Vibe"
        }
    }

    /// One-line description shown under the selector.
    var summary: String {
        switch self {
        case .precise: "Chord-locked backing track from your exact chords, key and tempo."
        case .vibe: "Realistic rendered audio from a text prompt. Coming soon."
        }
    }

    /// Whether this mode can actually be submitted yet. `.vibe`'s backend doesn't exist.
    var isAvailable: Bool {
        switch self {
        case .precise: true
        case .vibe: false
        }
    }

    /// Value sent in the API request body's `Mode` field. The API parses this into its own
    /// `GenerationMode` enum (`Precise` / `Vibe`) case-insensitively.
    var apiValue: String {
        switch self {
        case .precise: "Precise"
        case .vibe: "Vibe"
        }
    }
}
