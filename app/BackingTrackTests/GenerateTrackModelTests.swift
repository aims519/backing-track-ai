import Foundation
import Testing
@testable import BackingTrack

// `@MainActor` because `GenerateTrackModel` is main-actor isolated — the tests drive it exactly
// as the view would.
@MainActor
struct GenerateTrackModelTests {

    @Test func cannotGenerateWithNoChords() {
        let model = GenerateTrackModel()
        #expect(model.canGenerate == false)
    }

    @Test func canGenerateOnceAChordIsAdded() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        #expect(model.canGenerate)
    }

    @Test func submitDoesNothingWhileIncomplete() {
        let model = GenerateTrackModel()
        #expect(model.submit() == nil)
        #expect(model.lastSubmission == nil)
    }

    @Test func buildRequestMapsFormStateOntoTheApiContract() {
        let model = GenerateTrackModel()
        model.keyRoot = .g
        model.tempo = 132.6
        model.timeSignature = TimeSignature(beatCount: 6, beatUnit: 8)
        model.selectedStyle = .jazz
        model.addChord(root: .g, quality: .major)
        model.addChord(root: .e, quality: .minor7)
        model.addChord(root: .a, quality: .minor)
        model.addChord(root: .d, quality: .dominant7)

        let request = model.buildRequest()

        #expect(request.mode == "Precise")
        #expect(request.tempo == 133) // slider Double rounded to whole BPM
        #expect(request.keyRoot == "G")
        #expect(request.chords == ["G", "Emin7", "Amin", "D7"])
        #expect(request.timeSignatureBeatCount == 6)
        #expect(request.timeSignatureBeatUnit == 8)
        #expect(request.style == "jazz")
    }

    @Test func styleNotesAreAppendedToTheStyleAndTrimmed() {
        let model = GenerateTrackModel()
        model.selectedStyle = .blues
        model.styleNotes = "  slow, brushes not sticks \n"
        #expect(model.composedStyle == "blues. slow, brushes not sticks")
    }

    @Test func whitespaceOnlyStyleNotesAreIgnored() {
        let model = GenerateTrackModel()
        model.selectedStyle = .funk
        model.styleNotes = "   "
        #expect(model.composedStyle == "funk")
    }

    @Test func chordsReorderViaOnMoveOffsets() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .g, quality: .major)

        model.moveChords(fromOffsets: IndexSet(integer: 2), toOffset: 0)

        #expect(model.chords.map(\.apiSymbol) == ["G", "C", "F"])
    }

    @Test func chordsDeleteViaOnDeleteOffsets() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)

        model.removeChords(atOffsets: IndexSet(integer: 0))

        #expect(model.chords.map(\.apiSymbol) == ["F"])
    }

    @Test func submitRecordsTheBuiltRequest() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)

        let returned = model.submit()

        #expect(returned != nil)
        #expect(model.lastSubmission == returned)
    }

    @Test func vibeModeIsNotSubmittableYet() {
        #expect(GenerationMode.vibe.isAvailable == false)
        #expect(GenerationMode.precise.isAvailable)

        let model = GenerateTrackModel()
        model.mode = .vibe
        model.addChord(root: .c, quality: .major)
        // Even with a valid chord list, an unavailable mode can't be generated.
        #expect(model.canGenerate == false)
    }
}
