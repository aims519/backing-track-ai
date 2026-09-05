import Foundation
import SwiftData

/// One row of local job/track history: a generation the app submitted, mirrored from the poll
/// loop so it survives app relaunch and (once the player screen caches track content) plays back
/// with no backend/Tailscale connectivity.
///
/// Deliberately *not* the same type as `GenerationJob` — that's the wire DTO, and SwiftData needs
/// a reference type it can track. `JobHistoryStore` maps between the two.
///
/// Ids are stored as `String` (`uuidString`), not `UUID`: `#Predicate` on `UUID` equality has
/// been unreliable, and fetch-by-id is the store's hot path.
@Model
final class JobHistoryEntry {
    /// The job id the API issued, as `uuidString`. Uniqueness is enforced by `JobHistoryStore`
    /// fetching by this value before inserting: an `@Attribute(.unique)` constraint traps against
    /// the in-memory store the tests use, and buys little for a handful of rows.
    var jobIDString: String

    /// When the client submitted. Set once, never rewritten — the history list sorts on this.
    var createdAt: Date

    /// The most recent status seen from the server, as the backend's `JobStates` name (see
    /// `JobStatus.wireValue`). A raw string rather than an encoded enum so `JobStatus.unknown`
    /// round-trips its captured value.
    var statusWireValue: String

    /// What the client did with the job — see `JobDisposition`. The piece ADR 0023 flagged as
    /// having nowhere to live.
    var dispositionRawValue: String

    /// Description of the last poll error. Diagnostics only.
    var lastClientError: String?

    // MARK: Request parameters (captured at submit)

    // `GenerationJob` echoes all of these back except `style`, which only exists on
    // `GenerateTrackViewModel.composedStyle` — captured here so the history row is self-contained.
    var mode: String
    var tempo: Int
    var chordSymbols: [String]
    var keyRoot: String
    var timeSignatureBeatCount: Int
    var timeSignatureBeatUnit: Int
    var style: String

    // MARK: Server-side facts

    var startedAt: Date?
    var finishedAt: Date?
    /// The id for `GET /tracks/download/{trackId}`, as `uuidString`. `nil` until the job produces
    /// a track.
    var trackIDString: String?

    // MARK: Track content — schema room only

    // Populated by the player-screen task, which owns download + decoding. All nullable, so
    // adding the write path later is a SwiftData lightweight migration, not a rework.

    /// `"preciseMidiJson"` or `"vibeAudio"` once content is cached; `nil` before then. Explicit
    /// rather than derived from `mode` — ADR 0014 keeps the output format unpinned.
    var contentKind: String?
    /// Precise mode: the per-track MIDI/tempo JSON. Whoever populates this can move it to
    /// `@Attribute(.externalStorage)` then — the column is empty until they do.
    var preciseContentData: Data?
    /// Vibe mode: the file name (not a path — the container moves between installs) of the cached
    /// audio under Application Support.
    var cachedAudioFilename: String?
    var contentCachedAt: Date?

    init(
        jobIDString: String,
        createdAt: Date = .now,
        statusWireValue: String,
        dispositionRawValue: String,
        mode: String,
        tempo: Int,
        chordSymbols: [String],
        keyRoot: String,
        timeSignatureBeatCount: Int,
        timeSignatureBeatUnit: Int,
        style: String,
        startedAt: Date? = nil,
        finishedAt: Date? = nil,
        trackIDString: String? = nil
    ) {
        self.jobIDString = jobIDString
        self.createdAt = createdAt
        self.statusWireValue = statusWireValue
        self.dispositionRawValue = dispositionRawValue
        self.mode = mode
        self.tempo = tempo
        self.chordSymbols = chordSymbols
        self.keyRoot = keyRoot
        self.timeSignatureBeatCount = timeSignatureBeatCount
        self.timeSignatureBeatUnit = timeSignatureBeatUnit
        self.style = style
        self.startedAt = startedAt
        self.finishedAt = finishedAt
        self.trackIDString = trackIDString
    }

    // MARK: Typed bridges
    //
    // Computed, non-stored — a `#Predicate` must filter on `statusWireValue` / `dispositionRawValue`
    // / `jobIDString`, not these.

    /// Always non-`nil` in practice — `jobIDString` is only ever written from a real `UUID`.
    var jobID: UUID? { UUID(uuidString: jobIDString) }

    var trackID: UUID? { trackIDString.flatMap(UUID.init(uuidString:)) }

    var status: JobStatus {
        get { JobStatus(wireValue: statusWireValue) }
        set { statusWireValue = newValue.wireValue }
    }

    var disposition: JobDisposition {
        get { JobDisposition(rawValue: dispositionRawValue) }
        set { dispositionRawValue = newValue.rawValue }
    }
}

#if DEBUG
extension JobHistoryEntry {
    /// A representative row for `#Preview`s and tests — `status` / `disposition` default to a
    /// finished-and-ready job; override any field for other states.
    static func preview(
        jobID: UUID = UUID(),
        createdAt: Date = .now,
        status: JobStatus = .complete,
        disposition: JobDisposition = .ready,
        lastClientError: String? = nil,
        mode: String = "Precise",
        tempo: Int = 120,
        chordSymbols: [String] = ["C", "Amin", "F", "G7"],
        keyRoot: String = "C",
        timeSignatureBeatCount: Int = 4,
        timeSignatureBeatUnit: Int = 4,
        style: String = "Blues - slow, brushes",
        startedAt: Date? = Date(timeIntervalSinceNow: -40),
        finishedAt: Date? = Date(timeIntervalSinceNow: -12),
        trackIDString: String? = UUID().uuidString
    ) -> JobHistoryEntry {
        let entry = JobHistoryEntry(
            jobIDString: jobID.uuidString,
            createdAt: createdAt,
            statusWireValue: status.wireValue,
            dispositionRawValue: disposition.rawValue,
            mode: mode,
            tempo: tempo,
            chordSymbols: chordSymbols,
            keyRoot: keyRoot,
            timeSignatureBeatCount: timeSignatureBeatCount,
            timeSignatureBeatUnit: timeSignatureBeatUnit,
            style: style,
            startedAt: startedAt,
            finishedAt: finishedAt,
            trackIDString: trackIDString
        )
        entry.lastClientError = lastClientError
        return entry
    }
}
#endif
