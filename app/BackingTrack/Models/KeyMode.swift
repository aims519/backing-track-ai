import Foundation

/// The seven church modes the API's `ScaleMatcher` validates a track's harmony against.
/// Mirrors `KeyMode` in `api/src/BackingTrack.Generation/Jobs/GenerationJob.cs`.
enum KeyMode: String, CaseIterable, Identifiable, Sendable {
    case ionian
    case dorian
    case phrygian
    case lydian
    case mixolydian
    case aeolian
    case locrian

    var id: String { rawValue }

    /// Label shown in the key mode picker.
    var displayName: String {
        switch self {
        case .ionian: "Ionian (Major)"
        case .dorian: "Dorian"
        case .phrygian: "Phrygian"
        case .lydian: "Lydian"
        case .mixolydian: "Mixolydian"
        case .aeolian: "Aeolian (Minor)"
        case .locrian: "Locrian"
        }
    }
    
    // Compact form to fit on one line (e.g. history row)
    var shortName: String {
        switch self {
        case .ionian: "Major"
        case .dorian: "Dorian"
        case .phrygian: "Phrygian"
        case .lydian: "Lydian"
        case .mixolydian: "Mixolydian"
        case .aeolian: "Minor"
        case .locrian: "Locrian"
        }
    }

    /// Value sent in the API request body's `KeyMode` field, and parsed with the case-sensitive
    /// `Enum.TryParse<KeyMode>` overload (`GenerationRequestDto.Validate`) — the exact casing here
    /// matters.
    var apiValue: String {
        switch self {
        case .ionian: "Ionian"
        case .dorian: "Dorian"
        case .phrygian: "Phrygian"
        case .lydian: "Lydian"
        case .mixolydian: "Mixolydian"
        case .aeolian: "Aeolian"
        case .locrian: "Locrian"
        }
    }
}
