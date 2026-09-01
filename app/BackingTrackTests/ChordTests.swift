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

    @Test func everyQualityMapsToASuffixTheApiAccepts() {
        // The exact set of chord-type ids in api/.../KnowledgeBase/Content/chords.jsonl, plus the
        // empty string (a bare root, which the parser treats as major).
        let acceptedByApi: Set<String> = ["", "min", "7", "maj7", "min7", "sus2", "sus4", "dim", "6", "9"]
        for quality in ChordQuality.allCases {
            #expect(acceptedByApi.contains(quality.apiSuffix), "\(quality) produced \(quality.apiSuffix)")
        }
    }

    @Test func repeatedChordsAreDistinctRows() {
        // Same root + quality, but two separate list entries — so `id` must not be derived from
        // the musical content.
        let first = Chord(root: .c, quality: .major)
        let second = Chord(root: .c, quality: .major)
        #expect(first.id != second.id)
    }
}
