import Foundation

/// A time signature — `beatCount` beats per bar, each a `1/beatUnit` note.
///
/// The API validates `beatCount` in 2...32 and `beatUnit` in {2, 4, 8, 16, 32}. The UI offers the
/// handful of `common` signatures up front and only exposes the full ranges behind a "Custom"
/// option, so a nonsense signature like 31/32 takes deliberate effort to enter.
struct TimeSignature: Hashable, Sendable {
    var beatCount: Int
    var beatUnit: Int

    /// e.g. `"4/4"`.
    var displayName: String { "\(beatCount)/\(beatUnit)" }

    /// Beat-unit values the API accepts, in the order they should appear in the custom picker.
    static let allowedBeatUnits = [2, 4, 8, 16, 32]

    /// Beat-count values the API accepts.
    static let allowedBeatCounts = Array(2...32)

    /// The quick-pick signatures shown before "Custom". Covers the overwhelming majority of
    /// popular-music backing tracks.
    static let common: [TimeSignature] = [
        TimeSignature(beatCount: 4, beatUnit: 4),
        TimeSignature(beatCount: 3, beatUnit: 4),
        TimeSignature(beatCount: 6, beatUnit: 8),
        TimeSignature(beatCount: 2, beatUnit: 4),
        TimeSignature(beatCount: 5, beatUnit: 4),
        TimeSignature(beatCount: 7, beatUnit: 8),
        TimeSignature(beatCount: 12, beatUnit: 8),
    ]

    static let fourFour = TimeSignature(beatCount: 4, beatUnit: 4)

    /// Whether this signature is one of the `common` quick-picks (vs. a custom entry).
    var isCommon: Bool { Self.common.contains(self) }
}
