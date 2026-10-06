import Foundation
import Testing
@testable import BackingTrack

/// Counts calls into a `StubAPIClient`. An `actor` because the client is invoked off the main
/// actor; the `StubAPIClient` value type shares one by reference.
private actor CallLog {
    private(set) var submitCount = 0
    func recordSubmit() { submitCount += 1 }
}

/// Records calls to the injected notification-permission request, and whether the API had already
/// been hit at that point — the prompt must come before the submit, not race it.
@MainActor
private final class PermissionRequestLog {
    private(set) var requestCount = 0
    private(set) var submitCountAtFirstRequest: Int?

    func record(submitCountSoFar: Int) {
        requestCount += 1
        if submitCountAtFirstRequest == nil { submitCountAtFirstRequest = submitCountSoFar }
    }
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
    func cancelJob(id: UUID) async throws {
        fatalError("the generate flow never cancels")
    }
    func downloadTrackData(trackID: UUID) async throws -> Data {
        fatalError("the generate flow never downloads")
    }

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
    /// write to the on-disk SwiftData store. Likewise the permission request: the real one puts up
    /// the system prompt in the test host and waits for an answer nobody gives.
    private func readyModel(
        client: BackingTrackAPIClient = StubAPIClient.succeeding(),
        historyStore: JobHistoryStore = RecordingJobHistoryStore(),
        requestNotificationPermission: @escaping () async -> Void = {}
    ) -> GenerateTrackViewModel {
        let model = GenerateTrackViewModel(
            apiClient: client,
            historyStore: historyStore,
            requestNotificationPermission: requestNotificationPermission
        )
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
        let permissionLog = PermissionRequestLog()
        let model = GenerateTrackViewModel(
            apiClient: client,
            historyStore: store,
            requestNotificationPermission: { permissionLog.record(submitCountSoFar: 0) }
        )
        model.addChord(root: .c, quality: .major) // still no style

        await model.generate()

        #expect(model.submissionState == .idle)
        #expect(await client.callLog.submitCount == 0) // never reached the API
        #expect(store.upserts.isEmpty)                 // nothing written to history either
        #expect(permissionLog.requestCount == 0)       // and no permission prompt for a no-op tap
    }

    @Test func buildRequestMapsFormStateOntoTheApiContract() {
        let model = GenerateTrackViewModel()
        model.keyRoot = .g
        model.keyMode = .dorian
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
        #expect(request.keyMode == "Dorian")
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

    @Test func duplicateChordInsertsACopyStraightAfterTheOriginal() {
        let model = GenerateTrackViewModel()
        model.addChord(root: .c, quality: .major)
        model.addChord(root: .a, quality: .minor)
        model.addChord(root: .g, quality: .dominant7)

        model.duplicateChord(model.chords[1])

        #expect(model.chords.map(\.apiSymbol) == ["C", "Amin", "Amin", "G7"])
        #expect(model.chords[1].id != model.chords[2].id)
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

    @Test func generateAsksForNotificationPermissionBeforeSubmitting() async {
        let client = StubAPIClient.succeeding()
        let permissionLog = PermissionRequestLog()
        let model = readyModel(client: client, requestNotificationPermission: {
            permissionLog.record(submitCountSoFar: await client.callLog.submitCount)
        })

        await model.generate()

        #expect(permissionLog.requestCount == 1)
        #expect(permissionLog.submitCountAtFirstRequest == 0)
        #expect(await client.callLog.submitCount == 1)
    }

    @Test func buildRequestSendsTheCurrentDeviceToken() {
        // PushTokenStore is app-global, and the test host's AppDelegate may already have set it
        // from a real registration — restore whatever was there.
        let previous = PushTokenStore.deviceToken
        defer { PushTokenStore.deviceToken = previous }
        let model = GenerateTrackViewModel()

        PushTokenStore.deviceToken = "ab12"
        #expect(model.buildRequest().apnsDeviceToken == "ab12")

        PushTokenStore.deviceToken = nil
        #expect(model.buildRequest().apnsDeviceToken == nil)
    }

    @Test func acknowledgingTheResultReturnsToIdle() async {
        let model = readyModel(client: StubAPIClient.failing(.rateLimited(retryAfter: 30)))
        await model.generate()

        model.acknowledgeSubmissionResult()

        #expect(model.submissionState == .idle)
    }

    // MARK: - Opening a job from a notification

    @Test func openingANotifiedJobReturnsItsHistoryRow() {
        let store = RecordingJobHistoryStore()
        let entry = JobHistoryEntry.preview()
        store.stubbedEntry = entry
        let model = readyModel(historyStore: store)

        #expect(model.openJobFromNotification(UUID()) === entry)
    }

    @Test func openingANotifiedJobIsNilWhenHistoryCantBeRead() {
        let store = RecordingJobHistoryStore()
        store.errorToThrow = RecordingJobHistoryStore.StoreFailure()
        let model = readyModel(historyStore: store)

        // The view falls back to the history list rather than failing the tap.
        #expect(model.openJobFromNotification(UUID()) == nil)
    }

    @Test func openingANotifiedJobClosesTheProgressCover() async {
        let model = readyModel(client: StubAPIClient.succeeding(status: .inProgress))
        await model.generate()
        #expect(model.acceptedJob != nil) // cover is up

        _ = model.openJobFromNotification(UUID())

        #expect(model.acceptedJob == nil)
        #expect(model.submissionState == .idle)
    }

    @Test func openingANotifiedJobLeavesAFailedSubmissionAlone() async {
        let model = readyModel(client: StubAPIClient.failing(.unauthorized))
        await model.generate()

        _ = model.openJobFromNotification(UUID())

        // Only the progress cover is dismissed; a failure alert isn't the cover's to clear.
        #expect(model.submissionState == .failed(.unauthorized))
    }

    // MARK: - Style decomposition (inverse of `composedStyle`)

    @Test func decomposeStyleSplitsThePickedStyleFromTheNotes() {
        #expect(decomposeStyle("Blues - slow, brushes not sticks").0 == .blues)
        #expect(decomposeStyle("Blues - slow, brushes not sticks").1 == "slow, brushes not sticks")
    }

    @Test func decomposeStyleHandlesAStyleWithNoNotes() {
        let (style, notes) = decomposeStyle("Rock")
        #expect(style == .rock)
        #expect(notes.isEmpty)
    }

    @Test func decomposeStyleOnlySplitsOnTheFirstSeparator() {
        let (style, notes) = decomposeStyle("Jazz - laid back - a touch of swing")
        #expect(style == .jazz)
        #expect(notes == "laid back - a touch of swing")
    }

    @Test func decomposeStyleReturnsNilForANonPresetStyle() {
        let (style, notes) = decomposeStyle("Sludgecore - down-tuned")
        #expect(style == nil)
        #expect(notes.isEmpty)
    }

    @Test(arguments: MusicStyle.allCases)
    func decomposeStyleRoundTripsWhateverComposedStyleProduces(style: MusicStyle) {
        let model = GenerateTrackViewModel()
        model.selectedStyle = style
        model.styleNotes = "warm, roomy"

        let (decomposed, notes) = decomposeStyle(model.composedStyle)

        #expect(decomposed == style)
        #expect(notes == "warm, roomy")
    }

    // MARK: - Seeding the form from a history row

    @Test func applyMapsEveryFieldBackOntoTheForm() {
        let model = GenerateTrackViewModel()
        let entry = JobHistoryEntry.preview(
            mode: "Precise",
            tempo: 96,
            chordSymbols: ["G", "Emin7", "Amin", "D7"],
            keyRoot: "G",
            keyMode: "Dorian",
            timeSignatureBeatCount: 6,
            timeSignatureBeatUnit: 8,
            style: "Jazz - laid back"
        )

        model.apply(entry: entry)

        #expect(model.mode == .precise)
        #expect(model.keyRoot == .g)
        #expect(model.keyMode == .dorian)
        #expect(model.tempo == 96)
        #expect(model.timeSignature == TimeSignature(beatCount: 6, beatUnit: 8))
        #expect(model.chords.map(\.apiSymbol) == ["G", "Emin7", "Amin", "D7"])
        #expect(model.selectedStyle == .jazz)
        #expect(model.styleNotes == "laid back")
        #expect(model.seedWarning == nil)
        #expect(model.canGenerate)
    }

    @Test func applyReproducesTheOriginalRequestForACleanRow() {
        let original = GenerateTrackViewModel()
        original.keyRoot = .d
        original.keyMode = .mixolydian
        original.tempo = 140
        original.timeSignature = .fourFour
        original.selectedStyle = .funk
        original.styleNotes = "syncopated"
        original.addChord(root: .d, quality: .minor7)
        original.addChord(root: .g, quality: .dominant7)
        let request = original.buildRequest()
        let entry = JobHistoryEntry.preview(
            tempo: request.tempo,
            chordSymbols: request.chords,
            keyRoot: request.keyRoot,
            keyMode: request.keyMode,
            timeSignatureBeatCount: request.timeSignatureBeatCount,
            timeSignatureBeatUnit: request.timeSignatureBeatUnit,
            style: request.style
        )

        let seeded = GenerateTrackViewModel()
        seeded.apply(entry: entry)

        #expect(seeded.buildRequest() == request)
    }

    @Test func applyKeepsParseableChordsAndWarnsAboutTheRest() {
        let model = GenerateTrackViewModel()
        let entry = JobHistoryEntry.preview(chordSymbols: ["C", "Cm7", "F", "Bhalfdim"])

        model.apply(entry: entry)

        #expect(model.chords.map(\.apiSymbol) == ["C", "F"])
        #expect(model.seedWarning?.contains("Cm7") == true)
        #expect(model.seedWarning?.contains("Bhalfdim") == true)
    }

    @Test func applyDumpsANonPresetStyleIntoTheNotesField() {
        let model = GenerateTrackViewModel()
        let entry = JobHistoryEntry.preview(style: "Klezmer - frantic")

        model.apply(entry: entry)

        #expect(model.selectedStyle == nil)
        #expect(model.styleNotes == "Klezmer - frantic")
        #expect(model.seedWarning?.contains("notes field") == true)
    }

    @Test func applyRestoresAnUncommonTimeSignature() {
        let model = GenerateTrackViewModel()
        model.apply(entry: JobHistoryEntry.preview(timeSignatureBeatCount: 5, timeSignatureBeatUnit: 8))

        #expect(model.timeSignature == TimeSignature(beatCount: 5, beatUnit: 8))
        #expect(model.timeSignature.isCommon == false) // the view flips its Custom toggle on for this
    }

    @Test func submittingClearsTheSeedWarning() async {
        let model = readyModel()
        model.apply(entry: JobHistoryEntry.preview(chordSymbols: ["Cm7"], style: "Pop"))
        model.addChord(root: .c, quality: .major) // apply cleared the chords; give it something valid
        #expect(model.seedWarning != nil)

        await model.generate()

        #expect(model.seedWarning == nil)
    }

    /// Every field moved off its default, then cleared — must be indistinguishable from a new
    /// model. Also guards `hasEdits` and `clearForm()` against drifting from the initial values.
    @Test func clearFormMatchesAFreshModel() {
        let model = readyModel()
        model.mode = .vibe
        model.keyRoot = .fSharp
        model.keyMode = .dorian
        model.tempo = 90
        model.timeSignature = TimeSignature(beatCount: 5, beatUnit: 8)
        model.styleNotes = "brushes not sticks"
        model.seedWarning = "something didn't round-trip"
        #expect(model.hasEdits)

        model.clearForm()

        let fresh = GenerateTrackViewModel()
        #expect(model.hasEdits == false)
        #expect(model.mode == fresh.mode)
        #expect(model.buildRequest() == fresh.buildRequest())
        #expect(model.selectedStyle == nil)
        #expect(model.styleNotes.isEmpty)
        #expect(model.seedWarning == nil)
        #expect(model.canGenerate == false)
    }

    @Test func anUntouchedFormHasNoEdits() {
        #expect(GenerateTrackViewModel().hasEdits == false)
    }

    @Test func editingAnyFieldCountsAsAnEdit() {
        let edits: [(GenerateTrackViewModel) -> Void] = [
            { $0.keyRoot = .d },
            { $0.keyMode = .aeolian },
            { $0.tempo = 121 },
            { $0.timeSignature = TimeSignature(beatCount: 3, beatUnit: 4) },
            { $0.selectedStyle = .jazz },
            { $0.styleNotes = "x" },
            { $0.addChord(root: .c, quality: .major) },
        ]
        for edit in edits {
            let model = GenerateTrackViewModel()
            edit(model)
            #expect(model.hasEdits)
        }
    }

    /// The point of #48: a submission result — success included — must not wipe the form.
    @Test func acknowledgingASubmissionKeepsTheFormPopulated() async {
        let model = readyModel()
        await model.generate()
        model.acknowledgeSubmissionResult()

        #expect(model.chords.count == 1)
        #expect(model.selectedStyle == .pop)
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
