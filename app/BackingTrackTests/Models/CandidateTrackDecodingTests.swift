import Foundation
import Testing
@testable import BackingTrack

struct CandidateTrackDecodingTests {

    /// Copied verbatim from a real persisted file
    /// (`/tmp/backing-track/tracks/9bafcb11-…json`): PascalCase keys, no `$type` discriminator,
    /// a non-integer `LengthInBeats`, and chord voicings carrying a `MidiNotes` array.
    private let realTrackJSON = Data("""
    {"LengthInBeats":15,"Drums":[{"MidiNote":36,"BeatPosition":0,"DurationInBeats":0.5,"Velocity":110},{"MidiNote":42,"BeatPosition":0.5,"DurationInBeats":0.5,"Velocity":70},{"MidiNote":38,"BeatPosition":2,"DurationInBeats":0.25,"Velocity":120}],"Bass":[{"MidiNote":38,"BeatPosition":0,"DurationInBeats":0.5,"Velocity":95},{"MidiNote":45,"BeatPosition":0.5,"DurationInBeats":0.5,"Velocity":85}],"ChordVoicings":[{"MidiNotes":[62,69,74],"BeatPosition":0,"DurationInBeats":5,"Velocity":85},{"MidiNotes":[67,73,74],"BeatPosition":5,"DurationInBeats":5,"Velocity":85}]}
    """.utf8)

    @Test func decodesARealTrackFile() throws {
        let track = try CandidateTrack.decode(from: realTrackJSON)

        #expect(track.lengthInBeats == 15)
        #expect(track.drums.count == 3)
        #expect(track.bass.count == 2)
        #expect(track.chordVoicings.count == 2)

        #expect(track.drums[0] == DrumNoteEvent(beatPosition: 0, durationInBeats: 0.5, midiNote: 36, velocity: 110))
        #expect(track.bass[1].midiNote == 45)
        #expect(track.chordVoicings[0].midiNotes == [62, 69, 74])
        #expect(track.chordVoicings[1].durationInBeats == 5)
    }

    @Test func aMissingInstrumentSectionDecodesAsEmpty() throws {
        let json = Data("""
        {"LengthInBeats":8,"Drums":[{"MidiNote":36,"BeatPosition":0,"DurationInBeats":1,"Velocity":100}]}
        """.utf8)

        let track = try CandidateTrack.decode(from: json)

        #expect(track.drums.count == 1)
        #expect(track.bass.isEmpty)
        #expect(track.chordVoicings.isEmpty)
    }

    @Test func aMissingLengthIsAHardError() {
        let json = Data(#"{"Drums":[],"Bass":[],"ChordVoicings":[]}"#.utf8)
        #expect(throws: DecodingError.self) {
            try CandidateTrack.decode(from: json)
        }
    }

    @Test func malformedJSONThrows() {
        #expect(throws: (any Error).self) {
            try CandidateTrack.decode(from: Data("not json".utf8))
        }
    }

    /// camelCase keys must NOT decode — the wire contract is PascalCase, and silently accepting
    /// both would hide a real drift.
    @Test func camelCaseKeysDoNotDecode() {
        let json = Data(#"{"lengthInBeats":8,"drums":[],"bass":[],"chordVoicings":[]}"#.utf8)
        #expect(throws: DecodingError.self) {
            try CandidateTrack.decode(from: json)
        }
    }
}
