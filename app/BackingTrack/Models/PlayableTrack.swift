import Foundation

/// A downloaded track plus the musical context the download itself doesn't carry.
///
/// `CandidateTrack` is pure note events in beat-space. Tempo, key, time signature and the
/// chord-per-bar labels all live on `GenerationJob` / `JobHistoryEntry`, so the player screen's
/// view model stitches the two together into this before handing it to the audio engine.
struct PlayableTrack: Sendable, Equatable {
    let trackID: UUID
    let track: CandidateTrack
    /// `job.tempo` — the "reset tempo" target for the slider.
    let originalTempo: Int
    /// `nil` when the stored root isn't one of the 12 known spellings (shouldn't happen for a
    /// real job, but `keyRoot` is a free string on the wire).
    let keyRoot: NoteName?
    let keyMode: String
    let timeSignature: TimeSignature
    /// One symbol per bar, in progression order (`job.chords`).
    let chordSymbols: [String]
    /// Parsed chords, index-aligned with `chordSymbols`. `nil` for a symbol the structured picker
    /// couldn't have produced — the strip still shows the raw symbol for those.
    let chords: [Chord?]
    /// Loop length ÷ bar count. Used to map the play head to a bar for the chord highlight.
    let beatsPerBar: Double
    /// Number of bars in the loop (`chordSymbols.count`, or a derived fallback).
    let barCount: Int
    let title: String?
    let style: MusicStyle?

    init(
        trackID: UUID,
        track: CandidateTrack,
        originalTempo: Int,
        keyRoot: NoteName?,
        keyMode: String,
        timeSignature: TimeSignature,
        chordSymbols: [String],
        title: String?,
        style: MusicStyle?
    ) {
        self.trackID = trackID
        self.track = track
        self.originalTempo = originalTempo
        self.keyRoot = keyRoot
        self.keyMode = keyMode
        self.timeSignature = timeSignature
        self.chordSymbols = chordSymbols
        self.chords = chordSymbols.map { Chord(apiSymbol: $0) }
        self.title = title
        self.style = style

        // The chord list is one symbol per bar, so it's the reliable bar count. Fall back to the
        // time signature only when the progression didn't round-trip (empty).
        if chordSymbols.isEmpty {
            let bars = max(1, Int((track.lengthInBeats / Double(timeSignature.beatCount)).rounded()))
            self.barCount = bars
            self.beatsPerBar = track.lengthInBeats / Double(bars)
        } else {
            self.barCount = chordSymbols.count
            self.beatsPerBar = track.lengthInBeats / Double(chordSymbols.count)
        }

        // The generator is meant to emit whole bars in the requested signature. A mismatch means
        // odd data (observed: a 15-beat loop over a 4/4 request) — play it anyway, keyed off the
        // loop length, but leave a breadcrumb.
        #if DEBUG
        let expected = Double(timeSignature.beatCount)
        if abs(beatsPerBar - expected) > 0.01 {
            print("⚠️ PlayableTrack \(trackID): \(track.lengthInBeats) beats over \(barCount) bars = \(beatsPerBar) beats/bar, expected \(expected)")
        }
        #endif
    }

    /// The bar (0-based) the play head sits in at `loopBeat` — a beat offset already reduced into
    /// `0..<lengthInBeats`. Drives the chord-progression highlight.
    func bar(atLoopBeat loopBeat: Double) -> Int {
        guard beatsPerBar > 0, barCount > 0 else { return 0 }
        return min(barCount - 1, max(0, Int(loopBeat / beatsPerBar)))
    }

    /// Whole beats in a bar — `beatsPerBar` rounded, since odd data can make that fractional.
    /// Sizes the count-in and the beat indicator.
    var beatsInBar: Int { max(1, Int(beatsPerBar.rounded())) }

    /// The beat (0-based) within its bar at `loopBeat`, clamped to `0..<beatsInBar` so a
    /// fractional `beatsPerBar` never produces a beat the indicator has no dot for.
    func beat(atLoopBeat loopBeat: Double) -> Int {
        let beatInBar = loopBeat - Double(bar(atLoopBeat: loopBeat)) * beatsPerBar
        return min(beatsInBar - 1, max(0, Int(beatInBar)))
    }

    /// Seconds one loop lasts at `bpm`.
    func loopDuration(atTempo bpm: Double) -> Double {
        guard bpm > 0 else { return 0 }
        return track.lengthInBeats / bpm * 60
    }
}

#if DEBUG
extension PlayableTrack {
    static func preview(
        chordSymbols: [String] = ["C", "Amin", "F", "G7"],
        timeSignature: TimeSignature = .fourFour,
        originalTempo: Int = 120,
        track: CandidateTrack = .preview(lengthInBeats: 16)
    ) -> PlayableTrack {
        PlayableTrack(
            trackID: UUID(),
            track: track,
            originalTempo: originalTempo,
            keyRoot: .c,
            keyMode: "Ionian",
            timeSignature: timeSignature,
            chordSymbols: chordSymbols,
            title: "Blues - slow, brushes",
            style: decomposeStyle("Blues - slow, brushes").0
        )
    }
}
#endif
