import Foundation
import Testing
@testable import BackingTrack

/// Counts calls into a `StubAPIClient`. An `actor` because the client is invoked off the main
/// actor; the `StubAPIClient` value type shares one by reference.
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
        StubAPIClient(result: .success(.preview(status: status)))
    }

    static func failing(_ error: APIClientError) -> StubAPIClient {
        StubAPIClient(result: .failure(error))
    }
}

@MainActor
struct GenerateTrackViewModelTests {

    /// A ready-to-submit model. The history store must be passed — the model's real default would
    /// write to the on-disk SwiftData store.
    private func readyModel(
        client: BackingTrackAPIClient = StubAPIClient.succeeding(),
        historyStore: JobHistoryStore = RecordingJobHistoryStore()
    ) -> GenerateTrackViewModel {
        let model = GenerateTrackViewModel(apiClient: client, historyStore: historyStore)
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .pop
        return model
    }

    @Test func cannotGenerateWithAnEmptyForm() {
        let model = GenerateTrackViewModel()
        #expect(model.canGenerate == false)
    }

    @Test func aChordAloneIsNotEnoughWithoutAStyle() {
        let model = GenerateTrackViewModel()
        model.addChord(root: .c, quality: .major)
        #expect(model.selectedStyle == nil)
        #expect(model.canGenerate == false)
    }

    @Test func canGenerateWithAtLeastOneChordAndAStyle() {
        let model = GenerateTrackViewModel()
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .rock
        #expect(model.canGenerate)
    }

    @Test func generateDoesNothingWhileIncomplete() async {
        let client = StubAPIClient.succeeding()
        let store = RecordingJobHistoryStore()
        let model = GenerateTrackViewModel(apiClient: client, historyStore: store)
        model.addChord(root: .c, quality: .major) // still no style

        await model.generate()

        #expect(model.submissionState == .idle)
        #expect(await client.callLog.submitCount == 0) // never reached the API
        #expect(store.upserts.isEmpty)                 // nothing written to history either
    }

    @Test func buildRequestMapsFormStateOntoTheApiContract() {
        let model = GenerateTrackViewModel()
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
        #expect(request.style == "Jazz")
    }

    @Test func styleNotesAreAppendedToTheStyleAndTrimmed() {
        let model = GenerateTrackViewModel()
        model.selectedStyle = .blues
        model.styleNotes = "  slow, brushes not sticks \n"
        #expect(model.composedStyle == "Blues - slow, brushes not sticks")
    }

    @Test func whitespaceOnlyStyleNotesAreIgnored() {
        let model = GenerateTrackViewModel()
        model.selectedStyle = .funk
        model.styleNotes = "   "
        #expect(model.composedStyle == "Funk")
    }

    @Test func notesWithNoStylePickedAreNotSentAsAStyle() {
        let model = GenerateTrackViewModel()
        model.styleNotes = "slow shuffle"
        #expect(model.composedStyle.isEmpty)
    }

    @Test func removeChordDeletesByIdentityNotPosition() {
        let model = GenerateTrackViewModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .c, quality: .major) // a repeat — must not also remove this one

        model.removeChord(model.chords[0])

        #expect(model.chords.map(\.apiSymbol) == ["F", "C"])
    }

    @Test func moveChordReordersTheProgression() {
        let model = GenerateTrackViewModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .f, quality: .major)
        model.addChord(root: .g, quality: .major)

        model.moveChord(withID: model.chords[0].id, toIndex: 3) // C to the end

        #expect(model.chords.map(\.apiSymbol) == ["F", "G", "C"])
    }

    @Test func styleAloneIsSentWhenNoNotesAreAdded() {
        let model = GenerateTrackViewModel()
        model.selectedStyle = .reggae
        #expect(model.composedStyle == "Reggae")
    }

    @Test func composedStyleIsEmptyWithNoStylePicked() {
        let model = GenerateTrackViewModel()
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

    @Test func generateWritesTheAcceptedJobToHistory() async {
        let store = RecordingJobHistoryStore()
        let model = readyModel(client: StubAPIClient.succeeding(status: .pending), historyStore: store)
        model.styleNotes = "slow shuffle"

        await model.generate()

        guard case .submitted(let job) = model.submissionState else {
            Issue.record("expected .submitted, got \(model.submissionState)")
            return
        }
        #expect(store.upserts.count == 1)
        #expect(store.upserts.first?.job.id == job.id)
        #expect(store.upserts.first?.style == model.composedStyle) // "Pop - slow shuffle"
    }

    @Test func aFailedSubmissionWritesNothingToHistory() async {
        let store = RecordingJobHistoryStore()
        let model = readyModel(client: StubAPIClient.failing(.unauthorized), historyStore: store)

        await model.generate()

        #expect(store.upserts.isEmpty)
    }

    @Test func aStoreFailureDoesNotBreakSubmission() async {
        let store = RecordingJobHistoryStore()
        store.errorToThrow = RecordingJobHistoryStore.StoreFailure()
        let model = readyModel(client: StubAPIClient.succeeding(status: .pending), historyStore: store)

        await model.generate()

        // The write was attempted and threw, but the submission still succeeded.
        #expect(store.upserts.count == 1)
        guard case .submitted = model.submissionState else {
            Issue.record("expected .submitted, got \(model.submissionState)")
            return
        }
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

        let model = GenerateTrackViewModel()
        model.mode = .vibe
        model.addChord(root: .c, quality: .major)
        model.selectedStyle = .rock
        // Chord list and style are both valid — an unavailable mode is the only thing blocking.
        #expect(model.canGenerate == false)
    }
}
