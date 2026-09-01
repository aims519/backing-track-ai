import Foundation

/// A note name, used both for the track's key root and for chord roots.
///
/// Sharp spelling only (no `Db`, `Eb`, …) to keep the picker to 12 unambiguous choices — the API
/// accepts either spelling, so nothing is lost on the wire.
enum NoteName: String, CaseIterable, Identifiable, Sendable {
    case c = "C"
    case cSharp = "C#"
    case d = "D"
    case dSharp = "D#"
    case e = "E"
    case f = "F"
    case fSharp = "F#"
    case g = "G"
    case gSharp = "G#"
    case a = "A"
    case aSharp = "A#"
    case b = "B"

    var id: String { rawValue }

    /// How the note reads in the UI. Same as the raw value, but uses the ♯ glyph rather than an
    /// ASCII `#` so it looks like real notation.
    var displayName: String { rawValue.replacingOccurrences(of: "#", with: "\u{266F}") }
}

/// The chord qualities the API's `ChordSymbolParser` understands. This list is deliberately the
/// exact set of chord-type ids in `api/.../KnowledgeBase/Content/chords.jsonl` — anything outside
/// it is rejected server-side, so the picker only ever offers these.
enum ChordQuality: String, CaseIterable, Identifiable, Sendable {
    case major
    case minor
    case dominant7
    case major7
    case minor7
    case sus2
    case sus4
    case diminished
    case sixth
    case ninth

    var id: String { rawValue }

    /// Full name shown in the "add chord" picker.
    var pickerName: String {
        switch self {
        case .major: "Major"
        case .minor: "Minor"
        case .dominant7: "Dominant 7th"
        case .major7: "Major 7th"
        case .minor7: "Minor 7th"
        case .sus2: "Suspended 2nd"
        case .sus4: "Suspended 4th"
        case .diminished: "Diminished"
        case .sixth: "6th"
        case .ninth: "9th"
        }
    }

    /// The suffix in the musician-facing chord symbol (e.g. the `m7` in `Am7`). Major has none.
    var displaySuffix: String {
        switch self {
        case .major: ""
        case .minor: "m"
        case .dominant7: "7"
        case .major7: "maj7"
        case .minor7: "m7"
        case .sus2: "sus2"
        case .sus4: "sus4"
        case .diminished: "dim"
        case .sixth: "6"
        case .ninth: "9"
        }
    }

    /// The suffix the API expects (its `chords.jsonl` ids). Differs from `displaySuffix` for
    /// minor chords: the API keys on `min` / `min7`, not the `m` / `m7` a musician would write.
    /// Major sends an empty suffix — the parser treats a bare root (`"C"`) as major.
    var apiSuffix: String {
        switch self {
        case .major: ""
        case .minor: "min"
        case .dominant7: "7"
        case .major7: "maj7"
        case .minor7: "min7"
        case .sus2: "sus2"
        case .sus4: "sus4"
        case .diminished: "dim"
        case .sixth: "6"
        case .ninth: "9"
        }
    }
}

/// One chord in the progression: a root note plus a quality.
///
/// `id` is a fresh `UUID` per instance rather than being derived from root+quality, so the same
/// chord can appear twice in a progression (e.g. `C … C`) and still be two distinct, individually
/// draggable/deletable rows in the list.
struct Chord: Identifiable, Hashable, Sendable {
    let id: UUID
    var root: NoteName
    var quality: ChordQuality

    init(root: NoteName, quality: ChordQuality, id: UUID = UUID()) {
        self.id = id
        self.root = root
        self.quality = quality
    }

    /// Musician-facing symbol, e.g. `Am7`, `Csus4`, `F\u{266F}`.
    var displaySymbol: String { root.displayName + quality.displaySuffix }

    /// Symbol sent in the API request's `Chords` array, e.g. `Amin7`, `Csus4`, `F#`.
    var apiSymbol: String { root.rawValue + quality.apiSuffix }
}

extension [Chord] {
    /// Returns a copy with the chord identified by `id` moved so it lands at `targetIndex` in
    /// reading order. `targetIndex` is measured against the array *including* the dragged chord
    /// (that's what the chip flow's drop handler computes from the drop location), so this
    /// adjusts for the chord's own removal. Out-of-range indices are clamped; an unknown `id`
    /// leaves the array unchanged.
    ///
    /// A free function on `[Chord]` rather than a `GenerateTrackModel` method: SwiftUI's drop
    /// callback isn't guaranteed to run through the model's `@MainActor`, so the reorder math
    /// deliberately has no actor isolation to cross — it's pure array manipulation.
    func movingChord(withID id: UUID, toIndex targetIndex: Int) -> [Chord] {
        guard let fromIndex = firstIndex(where: { $0.id == id }) else { return self }

        var result = self
        let chord = result.remove(at: fromIndex)
        let adjusted = targetIndex > fromIndex ? targetIndex - 1 : targetIndex
        // `Swift.` qualified — bare `min`/`max` in an `Array` extension resolve to the
        // element-wise `Sequence.min()` / `.max()`, not the two-argument clamp functions.
        let clamped = Swift.min(Swift.max(adjusted, 0), result.count)
        result.insert(chord, at: clamped)
        return result
    }
}
