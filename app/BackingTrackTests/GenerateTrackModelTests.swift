import Foundation
import Testing
@testable import BackingTrack

/// Counts calls into a `StubAPIClient`. An `actor` because the client is invoked off the main
/// actor; the `StubAPIClient` struct shares one by reference.
private actor CallLog {
    private(set) var submitCount = 0
    func recordSubmit() { submitCount += 1 }
}

/// A `BackingTrackAPIClient` that returns a canned result — no network. `result` is set per test.
private struct StubAPIClient: BackingTrackAPIClient {
    var result: Result<GenerationJob, APIClientError>
    let callLog = CallLog()

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        await callLog.recordSubmit()
        return try result.get()
    }
    func jobStatus(id: UUID) async throws -> GenerationJob { try result.get() }

    static func succeeding(status: JobStatus = .pending) -> StubAPIClient {
        StubAPIClient(result: .success(GenerationJob(
            id: UUID(),
            status: status,
            startTime: Date(),
            endTime: nil,
            trackId: nil,
            audioLocation: nil,
            mode: "Precise",
            tempo: 120,
            chords: ["C"],
            keyRoot: "C",
            timeSignatureBeatCount: 4,
            timeSignatureBeatUnit: 4
        )))
    }

    static func failing(_ error: APIClientError) -> StubAPIClient {
        StubAPIClient(result: .failure(error))
    }
}

// `@MainActor` because `GenerateTrackModel` is main-actor isolated — the tests drive it exactly
// as the view would.
@MainActor
struct GenerateTrackModelTests {

    /// A ready-to-submit model wired to `client` (default: a stub that succeeds).
    private func readyModel(client: BackingTrackAPIClient = StubAPIClient.succeeding()) -> GenerateTrackModel {
        let model = GenerateTrackModel(apiClient: client)
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .pop
        return model
    }

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

    @Test func generateDoesNothingWhileIncomplete() async {
        let client = StubAPIClient.succeeding()
        let model = GenerateTrackModel(apiClient: client)
        model.addChord(root: .c, quality: .major) // still no style

        await model.generate()

        #expect(model.submissionState == .idle)
        #expect(await client.callLog.submitCount == 0) // never reached the API
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

    @Test func notesWithNoStylePickedAreNotSentAsAStyle() {
        let model = GenerateTrackModel()
        model.styleNotes = "slow shuffle"
        #expect(model.composedStyle.isEmpty)
    }

    @Test func removeChordDeletesByIdentityNotPosition() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .c, quality: .major) // a repeat — must not also remove this one

        model.removeChord(model.chords[0])

        #expect(model.chords.map(\.apiSymbol) == ["F", "C"])
    }

    @Test func moveChordReordersTheProgression() {
        let model = GenerateTrackModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .g, quality: .major)

        model.moveChord(withID: model.chords[0].id, toIndex: 3) // C to the end

        #expect(model.chords.map(\.apiSymbol) == ["F", "G", "C"])
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

    @Test func generateStoresTheAcceptedJobOnSuccess() async {
        let model = readyModel(client: StubAPIClient.succeeding(status: .pending))

        await model.generate()

        guard case .submitted(let job) = model.submissionState else {
            Issue.record("expected .submitted, got \(model.submissionState)")
            return
        }
        #expect(job.status == .pending)
    }

    @Test func generateStoresTheErrorOnFailure() async {
        let model = readyModel(client: StubAPIClient.failing(.unauthorized))

        await model.generate()

        #expect(model.submissionState == .failed(.unauthorized))
    }

    @Test func acknowledgingTheResultReturnsToIdle() async {
        let model = readyModel(client: StubAPIClient.failing(.rateLimited(retryAfter: 30)))
        await model.generate()

        model.acknowledgeSubmissionResult()

        #expect(model.submissionState == .idle)
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
