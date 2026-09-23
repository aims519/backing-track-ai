import Foundation
import Testing
@testable import BackingTrack

struct PlayableTrackTests {

    private func make(
        chordSymbols: [String],
        lengthInBeats: Double,
        timeSignature: TimeSignature = .fourFour
    ) -> PlayableTrack {
        PlayableTrack(
            trackID: UUID(),
            track: .preview(lengthInBeats: lengthInBeats),
            originalTempo: 120,
            keyRoot: .c,
            keyMode: "Ionian",
            timeSignature: timeSignature,
            chordSymbols: chordSymbols,
            title: nil
        )
    }

    @Test func barCountIsTheChordCountAndBeatsPerBarDividesTheLoop() {
        let track = make(chordSymbols: ["C", "Amin", "F", "G7"], lengthInBeats: 16)

        #expect(track.barCount == 4)
        #expect(track.beatsPerBar == 4)
    }

    @Test func beatsPerBarHandlesANonIntegerLoopLength() {
        // Odd real data: a 15-beat loop over a 4-chord progression.
        let track = make(chordSymbols: ["C", "F", "G"], lengthInBeats: 15)

        #expect(track.barCount == 3)
        #expect(track.beatsPerBar == 5)
    }

    @Test func chordParsingKeepsNilSlotsForUnparseableSymbols() throws {
        let track = make(chordSymbols: ["C", "Cadd9", "Amin7"], lengthInBeats: 12)

        #expect(track.chords.count == 3)
        // `Chord`'s identity is a per-instance UUID, so compare the musical content.
        let first = try #require(track.chords[0])
        #expect(first.root == .c && first.quality == .major)
        #expect(track.chords[1] == nil)                                   // "Cadd9" — unknown quality
        let third = try #require(track.chords[2])
        #expect(third.root == .a && third.quality == .minor7)
    }

    @Test func anEmptyProgressionFallsBackToTheTimeSignature() {
        let track = make(chordSymbols: [], lengthInBeats: 12, timeSignature: .fourFour)

        #expect(track.barCount == 3)
        #expect(track.beatsPerBar == 4)
    }

    @Test func barAtLoopBeatMapsThePlayHeadAndClamps() {
        let track = make(chordSymbols: ["C", "Amin", "F", "G7"], lengthInBeats: 16)

        #expect(track.bar(atLoopBeat: 0) == 0)
        #expect(track.bar(atLoopBeat: 4) == 1)
        #expect(track.bar(atLoopBeat: 15.9) == 3)
        #expect(track.bar(atLoopBeat: 99) == 3)   // clamped, not wrapped — caller reduces first
    }

    @Test func beatAtLoopBeatIsThePositionWithinTheBar() {
        let track = make(chordSymbols: ["C", "Amin", "F", "G7"], lengthInBeats: 16)

        #expect(track.beatsInBar == 4)
        #expect(track.beat(atLoopBeat: 0) == 0)
        #expect(track.beat(atLoopBeat: 1.5) == 1)
        #expect(track.beat(atLoopBeat: 4) == 0)      // downbeat of bar 2
        #expect(track.beat(atLoopBeat: 15.9) == 3)
        #expect(track.beat(atLoopBeat: 99) == 3)     // last bar clamps, so its beat does too
    }

    @Test func beatAtLoopBeatStaysInRangeForAFractionalBar() {
        // 14 beats over 4 bars = 3.5 beats/bar → rounds to 4 dots; the half-beat tail of each
        // bar must still land on a real dot.
        let track = make(chordSymbols: ["C", "Amin", "F", "G7"], lengthInBeats: 14)

        #expect(track.beatsInBar == 4)
        #expect(track.beat(atLoopBeat: 3.4) == 3)
        #expect(track.beat(atLoopBeat: 3.5) == 0)    // bar 2 starts at 3.5
        #expect(track.beat(atLoopBeat: 13.9) == 3)
    }

    @Test func loopDurationScalesWithTempo() {
        let track = make(chordSymbols: ["C", "F"], lengthInBeats: 8)

        #expect(track.loopDuration(atTempo: 120) == 4)   // 8 beats / 120bpm * 60
        #expect(track.loopDuration(atTempo: 60) == 8)
    }
}
