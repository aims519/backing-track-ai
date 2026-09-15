import Foundation
import Observation

/// Backing state for `GenerateTrackView` — the parameters of a Precise-mode backing track and
/// the logic that turns them into a `GenerationRequest`.
@MainActor
@Observable
final class GenerateTrackViewModel {

    @ObservationIgnored private let apiClient: BackingTrackAPIClient

    /// Durable job/track history. The default writes to the real on-disk store, so a test that
    /// drives `generate()` must pass a fake.
    @ObservationIgnored private let historyStore: JobHistoryStore

    init(
        apiClient: BackingTrackAPIClient = LiveBackingTrackAPIClient(),
        historyStore: JobHistoryStore = SwiftDataJobHistoryStore()
    ) {
        self.apiClient = apiClient
        self.historyStore = historyStore
    }

    // MARK: - Form state

    var mode: GenerationMode = .precise
    var keyRoot: NoteName = .c
    var keyMode: KeyMode = .ionian
    var chords: [Chord] = []
    /// Slider-friendly `Double`; rounded to a whole BPM when the request is built.
    var tempo: Double = 120
    var timeSignature: TimeSignature = .fourFour
    /// `nil` until the user picks a style — there's no default, so the choice is deliberate.
    var selectedStyle: MusicStyle?
    /// Optional free-text refinement appended to the picked style, e.g. "slow, brushes not sticks".
    var styleNotes: String = ""

    /// Set when the form was just seeded from a history row (`apply(entry:)`) and something didn't
    /// round-trip cleanly — an unparseable chord, or a style that isn't one of the presets.
    /// Shown as an inline note on the form; cleared on the next submit.
    var seedWarning: String?

    /// BPM range the API accepts (`GenerationRequestDto.Validate`).
    static let tempoRange: ClosedRange<Double> = Tempo.range

    // MARK: - Derived values

    var trimmedStyleNotes: String {
        styleNotes.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The `Style` string that will be sent: the picked style alone, or the style followed by the
    /// notes when the user has typed any. Empty until a style is picked — notes on their own
    /// aren't a meaningful style, and `canGenerate` blocks submission until a style is chosen.
    var composedStyle: String {
        guard let style = selectedStyle else { return "" }
        let notes = trimmedStyleNotes
        return notes.isEmpty ? style.apiValue.capitalized : "\(style.apiValue.capitalized) - \(notes)"
    }

    /// Whether the form has enough to submit. Tempo, key and time signature always hold a valid
    /// value from their controls, so only mode, chords and style can block.
    var canGenerate: Bool {
        mode.isAvailable && !chords.isEmpty && selectedStyle != nil
    }

    // MARK: - Chord list editing

    func addChord(root: NoteName, quality: ChordQuality) {
        chords.append(Chord(root: root, quality: quality))
    }

    /// Removes one chip by identity, not position — the chip flow has no stable index-based
    /// deletion the way a `List`'s `.onDelete` offsets would, and repeated chords are common.
    func removeChord(_ chord: Chord) {
        chords.removeAll { $0.id == chord.id }
    }

    /// Moves a chord to `targetIndex` in reading order. The index maths lives in the pure
    /// `[Chord].movingChord(withID:toIndex:)` helper so it can be tested without a model.
    func moveChord(withID id: UUID, toIndex targetIndex: Int) {
        chords = chords.movingChord(withID: id, toIndex: targetIndex)
    }

    // MARK: - Submission

    /// Maps the current form state onto the API request body. Kept separate from `submit()` so it
    /// can be unit-tested without any networking.
    func buildRequest() -> GenerationRequest {
        GenerationRequest(
            mode: mode.apiValue,
            tempo: Int(tempo.rounded()),
            chords: chords.map(\.apiSymbol),
            keyRoot: keyRoot.rawValue,
            keyMode: keyMode.apiValue,
            style: composedStyle,
            timeSignatureBeatCount: timeSignature.beatCount,
            timeSignatureBeatUnit: timeSignature.beatUnit
        )
    }

    /// Where the submit action is in its lifecycle. Covers getting a job *accepted* by the API;
    /// from `.submitted` on, `GenerationProgressView` polls it to completion.
    enum SubmissionState: Equatable {
        case idle
        case submitting
        /// The API accepted the request and created a job (still `Pending`/`InProgress` here).
        case submitted(GenerationJob)
        case failed(APIClientError)
    }

    private(set) var submissionState: SubmissionState = .idle

    var isSubmitting: Bool { submissionState == .submitting }

    /// Submits the current form to `POST /tracks/generate`. Safe to call only when `canGenerate`;
    /// the view gates the button on that. Ignored if a submission is already in flight.
    func generate() async {
        guard canGenerate, submissionState != .submitting else { return }
        seedWarning = nil
        submissionState = .submitting
        do {
            let job = try await apiClient.submitGeneration(buildRequest())
            submissionState = .submitted(job)
            // Written at acceptance, not at completion, so the job is in history even if the
            // progress screen times out or the app is killed first. Style is passed explicitly —
            // `GenerationJob` doesn't echo it back.
            recordToHistory { try historyStore.upsertOnSubmit(job: job, style: composedStyle) }
        } catch is CancellationError {
            submissionState = .idle
        } catch let error as APIClientError {
            submissionState = .failed(error)
        } catch {
            // Unreachable: the client's only error type is `APIClientError`. Kept exhaustive so an
            // unexpected throw is asserted in debug and still surfaced (not swallowed) in release.
            assertionFailure("unexpected error from API client: \(error)")
            submissionState = .failed(.decoding(String(describing: error)))
        }
    }

    /// Called when the user dismisses the failure alert or the progress screen — returns to the
    /// editable form (still populated, so it doubles as "try again").
    func acknowledgeSubmissionResult() {
        submissionState = .idle
        progressViewModel = nil
    }

    // MARK: - Seeding from history

    /// Loads a past job's parameters back into the form ("Use these settings" on the history
    /// screen). Chords are rebuilt from their stored API symbols; any that can't be — a spelling
    /// the picker can't produce — are dropped and named in `seedWarning`. `style` is split back
    /// into the picked style plus notes; if the stored string isn't one of the presets the whole
    /// thing goes into the notes field.
    func apply(entry: JobHistoryEntry) {
        mode = GenerationMode(rawValue: entry.mode.lowercased()) ?? .precise
        keyRoot = NoteName(rawValue: entry.keyRoot) ?? .c
        keyMode = KeyMode(rawValue: entry.keyMode.lowercased()) ?? .ionian
        tempo = Double(entry.tempo)
        timeSignature = TimeSignature(
            beatCount: entry.timeSignatureBeatCount,
            beatUnit: entry.timeSignatureBeatUnit
        )

        let parsedChords = entry.chordSymbols.map { (symbol: $0, chord: Chord(apiSymbol: $0)) }
        chords = parsedChords.compactMap(\.chord)

        let (style, notes) = Self.decomposeStyle(entry.style)
        selectedStyle = style
        styleNotes = style == nil ? entry.style : notes

        seedWarning = Self.seedWarning(
            unparsedChords: parsedChords.filter { $0.chord == nil }.map(\.symbol),
            styleUnmatched: style == nil && !entry.style.isEmpty
        )
        submissionState = .idle
    }

    /// Inverse of `composedStyle`: `"Blues - slow, brushes"` → `(.blues, "slow, brushes")`,
    /// `"Rock"` → `(.rock, "")`. Splits on the *first* `" - "` only, so notes may themselves
    /// contain that separator. A left token that isn't a known style yields `(nil, "")` — the
    /// caller then keeps the whole stored string as notes.
    static func decomposeStyle(_ stored: String) -> (MusicStyle?, String) {
        let token: String
        let notes: String
        if let separator = stored.range(of: " - ") {
            token = String(stored[..<separator.lowerBound])
            notes = String(stored[separator.upperBound...])
        } else {
            token = stored
            notes = ""
        }
        guard let style = MusicStyle(rawValue: token.lowercased()) else { return (nil, "") }
        return (style, notes)
    }

    private static func seedWarning(unparsedChords: [String], styleUnmatched: Bool) -> String? {
        var parts: [String] = []
        if !unparsedChords.isEmpty {
            let list = unparsedChords.joined(separator: ", ")
            parts.append("couldn't restore \(unparsedChords.count == 1 ? "chord" : "chords") \(list)")
        }
        if styleUnmatched {
            parts.append("the style isn't a preset, so it's in the notes field")
        }
        guard !parts.isEmpty else { return nil }
        return "Loaded from history — " + parts.joined(separator: "; ") + "."
    }

    /// Runs a history write, swallowing any failure: the store mirrors the generation flow rather
    /// than being part of it, so a store error must never fail a submission (ADR 0024). Logged,
    /// not trapped, so debug behaves the same as release.
    private func recordToHistory(_ write: () throws -> Void) {
        do {
            try write()
        } catch {
            #if DEBUG
            print("⚠️ job-history write failed (ignored): \(error)")
            #endif
        }
    }

    // MARK: - Progress screen

    /// The accepted job while the progress screen should be up, `nil` otherwise.
    var acceptedJob: GenerationJob? {
        if case .submitted(let job) = submissionState { job } else { nil }
    }

    /// Memoised: SwiftUI may re-evaluate the `.fullScreenCover` content closure while it's
    /// presented, and a fresh model each time would restart polling. Cleared on dismissal.
    @ObservationIgnored private var progressViewModel: GenerationProgressViewModel?

    /// The progress screen's model for `job` — keeps `apiClient` private to this type, and bundles
    /// in the composed style string (`GenerationJob` doesn't echo `Style` back).
    func makeProgressViewModel(for job: GenerationJob) -> GenerationProgressViewModel {
        if let progressViewModel, progressViewModel.initialJob.id == job.id {
            return progressViewModel
        }
        let model = GenerationProgressViewModel(
            job: job,
            styleSummary: composedStyle.isEmpty ? nil : composedStyle,
            apiClient: apiClient,
            historyStore: historyStore
        )
        progressViewModel = model
        return model
    }
}
