import Foundation
import Observation

/// Backing state for `GenerateTrackView` — the parameters of a Precise-mode backing track and
/// the logic that turns them into a `GenerationRequest`.
///
/// `@Observable` (iOS 17+) replaces the older `ObservableObject` / `@Published` pair: SwiftUI
/// tracks exactly which stored properties a view reads and re-renders only on those changes, with
/// no per-property annotation. `@MainActor` because every mutation here is driven from the UI and
/// read back by the view on the main thread — it also satisfies Swift 6's strict concurrency
/// checking without scattering `await` through the view.
@MainActor
@Observable
final class GenerateTrackViewModel {

    /// Talks to the API. Injectable so previews and tests run without the network — production
    /// gets the real `URLSession`-backed client.
    @ObservationIgnored private let apiClient: BackingTrackAPIClient

    /// Durable job/track history. Injectable for the same reason as `apiClient`; the default
    /// writes to the real on-disk store, so a test that drives `generate()` must pass a fake.
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
    var chords: [Chord] = []
    /// Slider-friendly `Double`; rounded to a whole BPM when the request is built.
    var tempo: Double = 120
    var timeSignature: TimeSignature = .fourFour
    /// `nil` until the user picks a style — there's no default, so the choice is deliberate.
    var selectedStyle: MusicStyle?
    /// Optional free-text refinement appended to the picked style, e.g. "slow, brushes not sticks".
    var styleNotes: String = ""

    /// BPM range the API accepts (`GenerationRequestDto.Validate`).
    static let tempoRange: ClosedRange<Double> = 40...240

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
    /// value from their controls, so the gates are: an available mode, at least one chord, and a
    /// style picked.
    var canGenerate: Bool {
        mode.isAvailable && !chords.isEmpty && selectedStyle != nil
    }

    // MARK: - Chord list editing

    func addChord(root: NoteName, quality: ChordQuality) {
        chords.append(Chord(root: root, quality: quality))
    }

    /// Removes one chip by identity — the chip flow has no stable index-based deletion the way a
    /// `List`'s `.onDelete` offsets would (chips reorder freely), so this is called with the
    /// specific `Chord` tapped rather than an `IndexSet`.
    func removeChord(_ chord: Chord) {
        chords.removeAll { $0.id == chord.id }
    }

    /// Moves a chord to `targetIndex` in reading order. Keeps reordering symmetrical with
    /// `addChord`/`removeChord` (the view drives all three through the model); the index maths
    /// lives in the pure `[Chord].movingChord(withID:toIndex:)` helper.
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
            style: composedStyle,
            timeSignatureBeatCount: timeSignature.beatCount,
            timeSignatureBeatUnit: timeSignature.beatUnit
        )
    }

    /// Where the submit action is in its lifecycle. Covers getting a job *accepted* by the API;
    /// once `.submitted`, `GenerateTrackView` presents `GenerationProgressView` (as a
    /// `fullScreenCover`) which polls the job to completion.
    enum SubmissionState: Equatable {
        case idle
        case submitting
        /// The API accepted the request and created a job (still `Pending`/`InProgress` here).
        case submitted(GenerationJob)
        case failed(APIClientError)
    }

    private(set) var submissionState: SubmissionState = .idle

    /// Drives the Generate button's spinner / disabled state.
    var isSubmitting: Bool { submissionState == .submitting }

    /// Submits the current form to `POST /tracks/generate`. Safe to call only when `canGenerate`;
    /// the view gates the button on that. Ignored if a submission is already in flight.
    func generate() async {
        guard canGenerate, submissionState != .submitting else { return }
        submissionState = .submitting
        do {
            let job = try await apiClient.submitGeneration(buildRequest())
            submissionState = .submitted(job)
            // Write the job into local history the moment it's accepted, so it's there even if
            // the progress screen times out or the app is killed before the job finishes. Style
            // is passed explicitly — `GenerationJob` doesn't echo it back.
            recordToHistory { try historyStore.upsertOnSubmit(job: job, style: composedStyle) }
        } catch is CancellationError {
            // The task backing this was cancelled (not a real failure) — drop back to the form.
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

    /// Runs a history write, swallowing any failure. Persistence is a durable mirror of the poll
    /// loop, not part of the generation flow — a store error must never fail a submission (ADR
    /// 0024). Logged, not trapped, so debug behaves the same as release.
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

    /// The accepted job while the progress screen should be up, `nil` otherwise. Drives the
    /// `.fullScreenCover(item:)` on `GenerateTrackView`.
    var acceptedJob: GenerationJob? {
        if case .submitted(let job) = submissionState { job } else { nil }
    }

    /// Memoised so the `.fullScreenCover` content closure — which SwiftUI may re-evaluate while
    /// presented — reuses one model rather than allocating a throwaway each time. Cleared when the
    /// screen is dismissed (`acknowledgeSubmissionResult`).
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
