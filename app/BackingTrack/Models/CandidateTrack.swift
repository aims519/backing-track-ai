import Foundation

/// The playable body of a completed Precise-mode job: MIDI-style note events, one progression
/// loop long, downloaded from `GET /tracks/download/{trackId}`.
///
/// Mirrors the API's `CandidateTrack` record (`api/src/BackingTrack.Generation/Steps/Types/`).
/// Unlike `GenerationJob`, the wire format is **PascalCase** — `CandidateTrackJsonContext` has no
/// camelCase naming policy — so every type here needs explicit `CodingKeys`, same as
/// `GenerationRequest`. The three instrument arrays arrive pre-split (no `$type` discriminator):
/// the endpoint serialises each element as its concrete type, never through the `NoteEvent` base.
///
/// All timing is in **beats**. Tempo, time signature, key and chord symbols are *not* here — they
/// come from `GenerationJob` / the history row. `PlayableTrack` combines the two.
struct CandidateTrack: Decodable, Equatable, Sendable {
    /// Total length of the single loop, in beats. Not guaranteed to be a whole number of bars —
    /// treat it as the authoritative loop point and derive bars from the chord count.
    let lengthInBeats: Double
    let drums: [DrumNoteEvent]
    let bass: [BassNoteEvent]
    let chordVoicings: [ChordVoicingEvent]

    enum CodingKeys: String, CodingKey {
        case lengthInBeats = "LengthInBeats"
        case drums = "Drums"
        case bass = "Bass"
        case chordVoicings = "ChordVoicings"
    }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        lengthInBeats = try container.decode(Double.self, forKey: .lengthInBeats)
        // A track missing an instrument section (e.g. no bass line) still decodes.
        drums = try container.decodeIfPresent([DrumNoteEvent].self, forKey: .drums) ?? []
        bass = try container.decodeIfPresent([BassNoteEvent].self, forKey: .bass) ?? []
        chordVoicings = try container.decodeIfPresent([ChordVoicingEvent].self, forKey: .chordVoicings) ?? []
    }

    /// Memberwise init — the custom `init(from:)` suppresses the synthesised one. For building
    /// fixtures in tests and previews.
    init(
        lengthInBeats: Double,
        drums: [DrumNoteEvent] = [],
        bass: [BassNoteEvent] = [],
        chordVoicings: [ChordVoicingEvent] = []
    ) {
        self.lengthInBeats = lengthInBeats
        self.drums = drums
        self.bass = bass
        self.chordVoicings = chordVoicings
    }

    /// Decodes a downloaded track body. Throws `DecodingError`; `TrackLoader` maps it to
    /// `TrackLoadError.decode`.
    static func decode(from data: Data) throws -> CandidateTrack {
        try JSONDecoder().decode(CandidateTrack.self, from: data)
    }
}

/// A single drum hit. `midiNote` is a General MIDI percussion number (36 kick, 38 snare,
/// 42 closed hi-hat, …).
struct DrumNoteEvent: Decodable, Equatable, Sendable {
    let beatPosition: Double
    let durationInBeats: Double
    let midiNote: Int
    let velocity: Int

    enum CodingKeys: String, CodingKey {
        case beatPosition = "BeatPosition"
        case durationInBeats = "DurationInBeats"
        case midiNote = "MidiNote"
        case velocity = "Velocity"
    }
}

/// A single bass note. `midiNote` is an absolute GM pitch.
struct BassNoteEvent: Decodable, Equatable, Sendable {
    let beatPosition: Double
    let durationInBeats: Double
    let midiNote: Int
    let velocity: Int

    enum CodingKeys: String, CodingKey {
        case beatPosition = "BeatPosition"
        case durationInBeats = "DurationInBeats"
        case midiNote = "MidiNote"
        case velocity = "Velocity"
    }
}

/// One sustained chord voicing — several pitches struck and released together.
struct ChordVoicingEvent: Decodable, Equatable, Sendable {
    let beatPosition: Double
    let durationInBeats: Double
    let midiNotes: [Int]
    let velocity: Int

    enum CodingKeys: String, CodingKey {
        case beatPosition = "BeatPosition"
        case durationInBeats = "DurationInBeats"
        case midiNotes = "MidiNotes"
        case velocity = "Velocity"
    }
}

#if DEBUG
extension CandidateTrack {
    /// A playable 4/4 loop for `#Preview`s and audio testing — a C–Am–F–G7 progression with a
    /// backbeat kit, a root/fifth bass line and a held chord voicing per bar. `lengthInBeats`
    /// sets the loop point; the pattern fills whole bars up to it (min one bar).
    static func preview(lengthInBeats: Double = 16) -> CandidateTrack {
        let bars = max(1, Int(lengthInBeats / 4))

        // Per-bar material, cycling through the four chords of the progression.
        let bassRoot   = [36, 33, 29, 31]   // C2, A1, F1, G1
        let bassFifth  = [43, 40, 36, 38]   // G2, E2, C2, D2
        let voicings   = [[60, 64, 67], [57, 60, 64], [53, 57, 60], [55, 59, 62, 65]]  // C, Am, F, G7

        var drums: [DrumNoteEvent] = []
        var bass: [BassNoteEvent] = []
        var chordVoicings: [ChordVoicingEvent] = []

        for bar in 0..<bars {
            let start = Double(bar) * 4
            let chord = bar % 4

            // Kick on 1 & 3, snare on 2 & 4, closed hat on every eighth.
            drums.append(DrumNoteEvent(beatPosition: start,     durationInBeats: 0.5, midiNote: 36, velocity: 105))
            drums.append(DrumNoteEvent(beatPosition: start + 2, durationInBeats: 0.5, midiNote: 36, velocity: 98))
            drums.append(DrumNoteEvent(beatPosition: start + 1, durationInBeats: 0.5, midiNote: 38, velocity: 115))
            drums.append(DrumNoteEvent(beatPosition: start + 3, durationInBeats: 0.5, midiNote: 38, velocity: 115))
            for eighth in 0..<8 {
                drums.append(DrumNoteEvent(
                    beatPosition: start + Double(eighth) * 0.5,
                    durationInBeats: 0.25, midiNote: 42, velocity: 62
                ))
            }

            bass.append(BassNoteEvent(beatPosition: start,     durationInBeats: 1, midiNote: bassRoot[chord],  velocity: 100))
            bass.append(BassNoteEvent(beatPosition: start + 2, durationInBeats: 1, midiNote: bassFifth[chord], velocity: 88))
            bass.append(BassNoteEvent(beatPosition: start + 3, durationInBeats: 1, midiNote: bassRoot[chord],  velocity: 92))

            chordVoicings.append(ChordVoicingEvent(
                beatPosition: start, durationInBeats: 4, midiNotes: voicings[chord], velocity: 50
            ))
        }

        return CandidateTrack(
            lengthInBeats: lengthInBeats,
            drums: drums,
            bass: bass,
            chordVoicings: chordVoicings
        )
    }
}
#endif
