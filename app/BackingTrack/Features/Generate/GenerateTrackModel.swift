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
final class GenerateTrackModel {

    /// Talks to the API. Injectable so previews and tests run without the network — production
    /// gets the real `URLSession`-backed client.
    @ObservationIgnored private let apiClient: BackingTrackAPIClient

    init(apiClient: BackingTrackAPIClient = LiveBackingTrackAPIClient()) {
        self.apiClient = apiClient
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
        return notes.isEmpty ? style.apiValue : "\(style.apiValue). \(notes)"
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

    /// Where the submit action is in its lifecycle. Only covers getting a job *accepted* by the
    /// API — polling that job to completion and the generating/ready/failed screens are separate
    /// tasks, built around `JobStatus` / the sketched state machine.
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

    /// Called when the user dismisses the result alert — returns to the editable form.
    func acknowledgeSubmissionResult() {
        submissionState = .idle
    }
}
