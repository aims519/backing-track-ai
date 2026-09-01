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
    /// notes when the user has typed any. Empty when no style is picked — `canGenerate` blocks
    /// submission in that case, so this is only ever sent with a style present.
    var composedStyle: String {
        let notes = trimmedStyleNotes
        guard let style = selectedStyle else { return notes }
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
