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

    // MARK: - Overlap merging

    @Test func aHeldChordAndItsRestrikeMergeIntoOneNotePerPitch() {
        // The real generator pattern: held voicing, then a soft same-pitch re-strike mid-bar.
        let track = CandidateTrack(
            lengthInBeats: 8,
            chordVoicings: [
                ChordVoicingEvent(beatPosition: 0, durationInBeats: 4, midiNotes: [57, 60, 64], velocity: 84),
                ChordVoicingEvent(beatPosition: 2, durationInBeats: 1, midiNotes: [57, 60, 64], velocity: 72),
            ]
        )

        let notes = PlayerSequenceBuilder.notes(for: .chords, in: track)

        // One note per pitch, spanning the full held duration — the short re-strike can't cut it.
        #expect(notes.count == 3)
        #expect(notes.allSatisfy { $0.beat == 0 && $0.durationInBeats == 4 && $0.velocity == 84 })
    }

    @Test func mergingExtendsToTheLaterOffsetAndKeepsTheLouderVelocity() {
        let notes = PlayerSequenceBuilder.merging([
            SequencedNote(beat: 0, key: 60, velocity: 70, durationInBeats: 2),
            SequencedNote(beat: 1, key: 60, velocity: 90, durationInBeats: 3),   // ends at 4
        ])

        #expect(notes == [SequencedNote(beat: 0, key: 60, velocity: 90, durationInBeats: 4)])
    }

    @Test func sequentialSamePitchNotesAreNotMerged() {
        // end == next start: adjacent, not overlapping.
        let notes = PlayerSequenceBuilder.merging([
            SequencedNote(beat: 0, key: 60, velocity: 80, durationInBeats: 1),
            SequencedNote(beat: 1, key: 60, velocity: 80, durationInBeats: 1),
        ])

        #expect(notes.count == 2)
    }

    @Test func overlappingDifferentPitchesAreLeftAlone() {
        let track = CandidateTrack(
            lengthInBeats: 4,
            chordVoicings: [
                ChordVoicingEvent(beatPosition: 0, durationInBeats: 4, midiNotes: [60, 64, 67], velocity: 80),
            ]
        )
        #expect(PlayerSequenceBuilder.notes(for: .chords, in: track).count == 3)
    }

    @Test func drumsAreNotMerged() {
        // Two kicks close together must both fire — a re-hit is a new hit.
        let track = CandidateTrack(
            lengthInBeats: 4,
            drums: [
                DrumNoteEvent(beatPosition: 0, durationInBeats: 0.5, midiNote: 36, velocity: 110),
                DrumNoteEvent(beatPosition: 0.25, durationInBeats: 0.5, midiNote: 36, velocity: 90),
            ]
        )
        #expect(PlayerSequenceBuilder.notes(for: .drums, in: track).count == 2)
    }
}
