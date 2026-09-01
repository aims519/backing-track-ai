import Foundation
import Testing
@testable import BackingTrack

// `@MainActor` because `GenerateTrackModel` is main-actor isolated — the tests drive it exactly
// as the view would.
@MainActor
struct GenerateTrackModelTests {

    @Test func cannotGenerateWithAnEmptyForm() {
        let model = GenerateTrackModel()
        #expect(model.canGenerate == false)
    }

    @Test func aChordAloneIsNotEnoughWithoutAStyle() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        #expect(model.selectedStyle == nil)
        #expect(model.canGenerate == false)
    }

    @Test func canGenerateWithAtLeastOneChordAndAStyle() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .rock
        #expect(model.canGenerate)
    }

    @Test func submitDoesNothingWhileIncomplete() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major) // still no style
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

    @Test func removeChordDeletesByIdentityNotPosition() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .c, quality: .major) // a repeat — must not also remove this one

        model.removeChord(model.chords[0])

        #expect(model.chords.map(\.apiSymbol) == ["F", "C"])
    }

    @Test func styleAloneIsSentWhenNoNotesAreAdded() {
        let model = GenerateTrackModel()
        model.selectedStyle = .reggae
        #expect(model.composedStyle == "reggae")
    }

    @Test func composedStyleIsEmptyWithNoStylePicked() {
        let model = GenerateTrackModel()
        #expect(model.composedStyle.isEmpty)
    }

    @Test func submitRecordsTheBuiltRequest() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .pop

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
        model.selectedStyle = .rock
        // Chord list and style are both valid — an unavailable mode is the only thing blocking.
        #expect(model.canGenerate == false)
    }
}
