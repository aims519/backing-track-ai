import Foundation
import Testing
@testable import BackingTrack

/// The chord model has to produce two different strings: what a musician reads (`Am7`) and what
/// the API's `ChordSymbolParser` accepts (`Amin7`). These pin that mapping down.
struct ChordTests {

    @Test func majorChordIsABareRoot() {
        let chord = Chord(root: .c, quality: .major)
        #expect(chord.apiSymbol == "C")
        #expect(chord.displaySymbol == "C")
    }

    @Test func minorUsesMinOnTheWireButMForDisplay() {
        let chord = Chord(root: .a, quality: .minor)
        #expect(chord.apiSymbol == "Amin")
        #expect(chord.displaySymbol == "Am")
    }

    @Test func minorSeventhFollowsTheSameSplit() {
        let chord = Chord(root: .d, quality: .minor7)
        #expect(chord.apiSymbol == "Dmin7")
        #expect(chord.displaySymbol == "Dm7")
    }

    @Test func sharpRootIsKeptAsAnAsciiHashOnTheWire() {
        let chord = Chord(root: .fSharp, quality: .dominant7)
        #expect(chord.apiSymbol == "F#7")
    }

    @Test func sharpRootRendersWithTheSharpGlyphForDisplay() {
        let chord = Chord(root: .fSharp, quality: .major)
        #expect(chord.displaySymbol == "F\u{266F}")
    }

    @Test func spokenNameSpellsTheQualityOutInFull() {
        #expect(Chord(root: .a, quality: .minor7).spokenName == "A Minor 7th")
        #expect(Chord(root: .c, quality: .major).spokenName == "C Major")
    }

    @Test func everyQualityMapsToASuffixTheApiAccepts() {
        // The exact set of chord-type ids in api/.../KnowledgeBase/Content/chords.jsonl, plus the
        // empty string (a bare root, which the parser treats as major).
        let acceptedByApi: Set<String> = ["", "min", "7", "maj7", "min7", "sus2", "sus4", "dim", "6", "9"]
        for quality in ChordQuality.allCases {
            #expect(acceptedByApi.contains(quality.apiSuffix), "\(quality) produced \(quality.apiSuffix)")
        }
    }

    @Test(arguments: NoteName.allCases, ChordQuality.allCases)
    func apiSymbolRoundTripsBackToTheSameChord(root: NoteName, quality: ChordQuality) {
        let original = Chord(root: root, quality: quality)
        let parsed = Chord(apiSymbol: original.apiSymbol)
        #expect(parsed?.root == root)
        #expect(parsed?.quality == quality)
    }

    @Test(arguments: [
        "Cm7",   // musician spelling, not the API's `min7`
        "Db",    // flat root — the picker is sharp-only
        "Cadd9", // a quality the picker doesn't offer
        "H",     // not a note
        "",      // empty
    ])
    func rejectsSymbolsThePickerCouldNotProduce(symbol: String) {
        #expect(Chord(apiSymbol: symbol) == nil)
    }

    @Test func parsesASharpRootWithAQuality() {
        let parsed = Chord(apiSymbol: "F#min7")
        #expect(parsed?.root == .fSharp)
        #expect(parsed?.quality == .minor7)
    }

    @Test func parsesABareSharpRootAsMajor() {
        let parsed = Chord(apiSymbol: "C#")
        #expect(parsed?.root == .cSharp)
        #expect(parsed?.quality == .major)
    }

    @Test func repeatedChordsAreDistinctRows() {
        // Same root + quality, but two separate list entries — so `id` must not be derived from
        // the musical content.
        let first = Chord(root: .c, quality: .major)
        let second = Chord(root: .c, quality: .major)
        #expect(first.id != second.id)
    }
}

/// `[Chord].movingChord(withID:toIndex:)` is the reorder math behind the chord chip flow's
/// drag-and-drop — a plain array operation (see `Chord.swift`) so it's testable with no view or
/// actor involved. `toIndex` is a reading-order position measured against the array *including*
/// the dragged chord, matching what the drop handler computes from the drop location.
struct ChordArrayReorderingTests {

    private let c = Chord(root: .c, quality: .major)
    private let f = Chord(root: .f, quality: .major)
    private let g = Chord(root: .g, quality: .major)
    private let a = Chord(root: .a, quality: .minor)

    @Test func movesAChordForward() {
        // [C, F, G], drop C at position 3 (after G) → [F, G, C]
        #expect([c, f, g].movingChord(withID: c.id, toIndex: 3).map(\.apiSymbol) == ["F", "G", "C"])
    }

    @Test func movesAChordBackward() {
        // [C, F, G], drop G at position 0 → [G, C, F]
        #expect([c, f, g].movingChord(withID: g.id, toIndex: 0).map(\.apiSymbol) == ["G", "C", "F"])
    }

    @Test func movesAMiddleChordForward() {
        // [C, F, G, Am], drop F at position 3 (between G and Am) → [C, G, F, Am]
        #expect([c, f, g, a].movingChord(withID: f.id, toIndex: 3).map(\.apiSymbol) == ["C", "G", "F", "Amin"])
    }

    @Test func movesAMiddleChordBackward() {
        // [C, F, G, Am], drop G at position 1 → [C, G, F, Am]
        #expect([c, f, g, a].movingChord(withID: g.id, toIndex: 1).map(\.apiSymbol) == ["C", "G", "F", "Amin"])
    }

    @Test func droppingAtAChordsOwnPositionIsANoOp() {
        #expect([c, f, g].movingChord(withID: f.id, toIndex: 1).map(\.apiSymbol) == ["C", "F", "G"])
        #expect([c, f, g].movingChord(withID: f.id, toIndex: 2).map(\.apiSymbol) == ["C", "F", "G"])
    }

    @Test func indicesAreClampedToTheArrayBounds() {
        #expect([c, f, g].movingChord(withID: c.id, toIndex: 99).map(\.apiSymbol) == ["F", "G", "C"])
        #expect([c, f, g].movingChord(withID: g.id, toIndex: -5).map(\.apiSymbol) == ["G", "C", "F"])
    }

    @Test func aChordIsIdentifiedByIdNotValueWhenReordering() {
        // Two chords with the same root/quality — must move the exact instance by id.
        let firstC = Chord(root: .c, quality: .major)
        let secondC = Chord(root: .c, quality: .major)

        let reordered = [firstC, f, secondC].movingChord(withID: secondC.id, toIndex: 0)

        #expect(reordered.map(\.id) == [secondC.id, firstC.id, f.id])
    }

    @Test func anUnknownIdLeavesTheArrayUnchanged() {
        #expect([c, f].movingChord(withID: UUID(), toIndex: 0) == [c, f])
    }
}
