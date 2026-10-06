import Foundation
import Testing
@testable import BackingTrack

/// The history row's chord line: musician-form symbols, capped at `HistoryRow.maxVisibleChords`
/// with a trailing `+N` for the rest.
@MainActor
@Suite("History row chords")
struct HistoryRowChordsTests {

    @Test func showsMusicianFormRatherThanApiSymbols() {
        #expect(HistoryRow.chordsDisplay(for: ["Amin7", "D7"]) == "Am7  D7")
    }

    @Test func exactlyTheCapHasNoSuffix() {
        #expect(HistoryRow.chordsDisplay(for: ["C", "F", "G", "C"]) == "C  F  G  C")
    }

    @Test func oneOverTheCapShowsPlusOne() {
        #expect(HistoryRow.chordsDisplay(for: ["C", "F", "G", "C", "G"]) == "C  F  G  C  +1")
    }

    @Test func aTwelveBarBluesCountsEveryHiddenChord() {
        let blues = ["A7", "A7", "A7", "A7", "D7", "D7", "A7", "A7", "E7", "D7", "A7", "E7"]
        #expect(HistoryRow.chordsDisplay(for: blues) == "A7  A7  A7  A7  +8")
    }

    @Test func symbolsThePickerCouldNotProduceAreShownRaw() {
        // Flat roots don't parse (the picker is sharp-only), so the stored string is kept.
        #expect(HistoryRow.chordsDisplay(for: ["Db", "Gb"]) == "Db  Gb")
    }

    @Test func noChordsGivesAnEmptyLine() {
        #expect(HistoryRow.chordsDisplay(for: []) == "")
    }
}
