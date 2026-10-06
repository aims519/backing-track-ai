import Foundation

/// The two generation modes from the project scope (`docs/backing-track-generator-scope.md`).
///
/// Only `.precise` is wired to the API today — `.vibe` (a third-party generative-audio service)
/// isn't built, so `isAvailable` is `false` for it and the generate form has no mode selector.
/// The case stays because the API's `Mode` field, history rows and the player's guard all
/// still need to represent it.
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

    /// Whether this mode can actually be submitted yet. `.vibe`'s backend doesn't exist.
    var isAvailable: Bool {
        switch self {
        case .precise: true
        case .vibe: false
        }
    }

    /// Value sent in the API request body's `Mode` field. The API parses it with the
    /// case-sensitive `Enum.TryParse<GenerationMode>` overload (`GenerationRequestDto.Validate`),
    /// so the exact casing here — `Precise` / `Vibe` — matters.
    var apiValue: String {
        switch self {
        case .precise: "Precise"
        case .vibe: "Vibe"
        }
    }
}
