import Foundation

/// One MIDI note placed on the timeline in beat-space — the AVFoundation-free description of a
/// single event `AudioEngine` schedules onto an `AVMusicTrack`.
struct SequencedNote: Equatable {
    var beat: Double
    /// MIDI note number, 0...127.
    var key: UInt8
    /// MIDI velocity, 0...127.
    var velocity: UInt8
    var durationInBeats: Double
}

/// Turns a `CandidateTrack`'s note-event arrays into the per-voice `SequencedNote` lists
/// `AudioEngine` schedules. Pure — no `AVFoundation`, no engine state — so the translation (the
/// part with the real bugs: chord expansion, MIDI range clamping, overlap merging) is
/// unit-testable on its own, and the engine is left doing only the `AVAudioSequencer` wiring.
enum PlayerSequenceBuilder {

    /// The notes for one voice. A chord voicing expands to one note per pitch. For the pitched
    /// voices, overlapping same-pitch notes are merged (see `merging`); drums are left alone —
    /// each hit is a one-shot and a re-hit should retrigger, not be swallowed.
    static func notes(for voice: PlaybackVoice, in track: CandidateTrack) -> [SequencedNote] {
        switch voice {
        case .bass:
            merging(track.bass.map {
                SequencedNote(
                    beat: $0.beatPosition,
                    key: midiValue($0.midiNote),
                    velocity: midiValue($0.velocity),
                    durationInBeats: $0.durationInBeats
                )
            })
        case .drums:
            track.drums.map {
                SequencedNote(
                    beat: $0.beatPosition,
                    key: midiValue($0.midiNote),
                    velocity: midiValue($0.velocity),
                    durationInBeats: $0.durationInBeats
                )
            }
        case .chords:
            merging(track.chordVoicings.flatMap { voicing in
                voicing.midiNotes.map {
                    SequencedNote(
                        beat: voicing.beatPosition,
                        key: midiValue($0),
                        velocity: midiValue(voicing.velocity),
                        durationInBeats: voicing.durationInBeats
                    )
                }
            })
        }
    }

    /// Merges any run of overlapping same-pitch notes into one note spanning from the first onset
    /// to the last offset, keeping the loudest velocity.
    ///
    /// The generator emits a held chord plus soft mid-bar re-strikes of the same voicing
    /// (`beat 0 dur 4` then `beat 2 dur 1`, same pitches). Scheduled literally, the short
    /// re-strike's note-off cuts the held note short — a real `AVAudioUnitSampler` retriggers the
    /// one voice per (channel, key), so the chord drops to silence for the rest of the bar. (The
    /// simulator's sampler allocates a second voice and rings through, which is why this only
    /// shows on device.) Merging costs the re-articulation but never the note.
    static func merging(_ notes: [SequencedNote]) -> [SequencedNote] {
        var byKey: [UInt8: [SequencedNote]] = [:]
        for note in notes { byKey[note.key, default: []].append(note) }

        var merged: [SequencedNote] = []
        for (_, keyNotes) in byKey {
            var run = keyNotes.sorted { $0.beat < $1.beat }[0]
            for next in keyNotes.sorted(by: { $0.beat < $1.beat }).dropFirst() {
                let runEnd = run.beat + run.durationInBeats
                if next.beat < runEnd {
                    run.durationInBeats = max(runEnd, next.beat + next.durationInBeats) - run.beat
                    run.velocity = max(run.velocity, next.velocity)
                } else {
                    merged.append(run)
                    run = next
                }
            }
            merged.append(run)
        }
        return merged.sorted { $0.beat != $1.beat ? $0.beat < $1.beat : $0.key < $1.key }
    }

    /// Clamps to the MIDI 0...127 range. The generator should stay well inside it, but `midiNote`
    /// and `velocity` are free `Int`s on the wire and an out-of-range value would trap the
    /// `UInt32` conversion in the sequencer.
    private static func midiValue(_ raw: Int) -> UInt8 {
        UInt8(min(127, max(0, raw)))
    }
}
