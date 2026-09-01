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

    // MARK: - Form state

    var mode: GenerationMode = .precise
    var keyRoot: NoteName = .c
    var chords: [Chord] = []
    /// Slider-friendly `Double`; rounded to a whole BPM when the request is built.
    var tempo: Double = 120
    var timeSignature: TimeSignature = .fourFour
    var selectedStyle: MusicStyle = .blues
    /// Optional free-text refinement appended to the picked style, e.g. "slow, brushes not sticks".
    var styleNotes: String = ""

    /// BPM range the API accepts (`GenerationRequestDto.Validate`).
    static let tempoRange: ClosedRange<Double> = 40...240

    // MARK: - Derived values

    var trimmedStyleNotes: String {
        styleNotes.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The `Style` string that will be sent: the picked style alone, or the style followed by the
    /// notes when the user has typed any.
    var composedStyle: String {
        let notes = trimmedStyleNotes
        return notes.isEmpty ? selectedStyle.apiValue : "\(selectedStyle.apiValue). \(notes)"
    }

    /// Whether the form has enough to submit. Tempo, key, time signature and style always hold a
    /// valid value from their controls, so the only gate is "at least one chord" — plus the mode
    /// being one that actually works yet.
    var canGenerate: Bool {
        mode.isAvailable && !chords.isEmpty
    }

    // MARK: - Chord list editing

    func addChord(root: NoteName, quality: ChordQuality) {
        chords.append(Chord(root: root, quality: quality))
    }

    /// Wired to the chord list's `.onMove` — drag-to-reorder.
    func moveChords(fromOffsets: IndexSet, toOffset: Int) {
        chords.move(fromOffsets: fromOffsets, toOffset: toOffset)
    }

    /// Wired to the chord list's `.onDelete` — swipe-to-delete.
    func removeChords(atOffsets offsets: IndexSet) {
        chords.remove(atOffsets: offsets)
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

    /// The last request `submit()` produced. Retained so the view can show a confirmation of what
    /// would be sent while the networking layer doesn't exist yet.
    private(set) var lastSubmission: GenerationRequest?

    /// Placeholder submit. The actual `POST /tracks/generate` call, the 429/rate-limit handling,
    /// and the generating → ready/fallback/failed screen are a separate task (the iOS state
    /// machine is already sketched). For now this just records what would be sent.
    @discardableResult
    func submit() -> GenerationRequest? {
        guard canGenerate else { return nil }
        let request = buildRequest()
        lastSubmission = request
        // TODO: hand `request` to the API client and navigate to the generating screen.
        print("Would POST /tracks/generate: \(request)")
        return request
    }
}
