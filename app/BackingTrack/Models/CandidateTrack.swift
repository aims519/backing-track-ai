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
    /// A tiny two-bar loop for `#Preview`s and tests.
    static func preview(lengthInBeats: Double = 8) -> CandidateTrack {
        let json = """
        {
          "LengthInBeats": \(lengthInBeats),
          "Drums": [
            {"MidiNote": 36, "BeatPosition": 0, "DurationInBeats": 0.5, "Velocity": 110},
            {"MidiNote": 38, "BeatPosition": 1, "DurationInBeats": 0.5, "Velocity": 120}
          ],
          "Bass": [
            {"MidiNote": 40, "BeatPosition": 0, "DurationInBeats": 1, "Velocity": 95}
          ],
          "ChordVoicings": [
            {"MidiNotes": [48, 52, 55], "BeatPosition": 0, "DurationInBeats": 4, "Velocity": 85}
          ]
        }
        """
        // Force-try: the literal above is a fixed, valid document.
        return try! CandidateTrack.decode(from: Data(json.utf8))
    }
}
#endif
