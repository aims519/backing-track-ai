import Testing
@testable import BackingTrack

struct PlayerSequenceBuilderTests {

    @Test func bassAndDrumEventsMapOneToOne() {
        let track = CandidateTrack(
            lengthInBeats: 8,
            drums: [
                DrumNoteEvent(beatPosition: 0, durationInBeats: 0.5, midiNote: 36, velocity: 100),
                DrumNoteEvent(beatPosition: 1, durationInBeats: 0.5, midiNote: 38, velocity: 110),
            ],
            bass: [
                BassNoteEvent(beatPosition: 0, durationInBeats: 1, midiNote: 40, velocity: 90),
            ]
        )

        #expect(PlayerSequenceBuilder.notes(for: .drums, in: track).count == 2)
        #expect(PlayerSequenceBuilder.notes(for: .bass, in: track).count == 1)

        let firstDrum = PlayerSequenceBuilder.notes(for: .drums, in: track)[0]
        #expect(firstDrum == SequencedNote(beat: 0, key: 36, velocity: 100, durationInBeats: 0.5))
    }

    @Test func chordVoicingExpandsToOneNotePerPitch() {
        let track = CandidateTrack(
            lengthInBeats: 8,
            chordVoicings: [
                ChordVoicingEvent(beatPosition: 2, durationInBeats: 4, midiNotes: [60, 64, 67], velocity: 75),
            ]
        )

        let notes = PlayerSequenceBuilder.notes(for: .chords, in: track)

        #expect(notes.count == 3)
        #expect(notes.map(\.key) == [60, 64, 67])
        // Every pitch inherits the voicing's start beat, duration and velocity.
        #expect(notes.allSatisfy { $0.beat == 2 && $0.durationInBeats == 4 && $0.velocity == 75 })
    }

    @Test func midiValuesAreClampedToTheValidRange() {
        let track = CandidateTrack(
            lengthInBeats: 4,
            bass: [
                BassNoteEvent(beatPosition: 0, durationInBeats: 1, midiNote: 200, velocity: 999),
                BassNoteEvent(beatPosition: 1, durationInBeats: 1, midiNote: -5, velocity: -10),
            ]
        )

        let notes = PlayerSequenceBuilder.notes(for: .bass, in: track)

        #expect(notes[0].key == 127 && notes[0].velocity == 127)
        #expect(notes[1].key == 0 && notes[1].velocity == 0)
    }

    @Test func aMissingVoiceYieldsNoNotes() {
        let track = CandidateTrack(lengthInBeats: 4, bass: [
            BassNoteEvent(beatPosition: 0, durationInBeats: 1, midiNote: 40, velocity: 90),
        ])

        #expect(PlayerSequenceBuilder.notes(for: .drums, in: track).isEmpty)
        #expect(PlayerSequenceBuilder.notes(for: .chords, in: track).isEmpty)
    }
}
