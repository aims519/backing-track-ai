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
/// part with the real bugs: chord expansion, MIDI range clamping) is unit-testable on its own,
/// and the engine is left doing only the `AVAudioSequencer` wiring around it.
enum PlayerSequenceBuilder {

    /// The notes for one voice, in the order the events appear on the wire. A chord voicing
    /// expands to one note per pitch, all sharing the voicing's start beat, duration and velocity.
    static func notes(for voice: PlaybackVoice, in track: CandidateTrack) -> [SequencedNote] {
        switch voice {
        case .bass:
            track.bass.map {
                SequencedNote(
                    beat: $0.beatPosition,
                    key: midiValue($0.midiNote),
                    velocity: midiValue($0.velocity),
                    durationInBeats: $0.durationInBeats
                )
            }
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
            track.chordVoicings.flatMap { voicing in
                voicing.midiNotes.map {
                    SequencedNote(
                        beat: voicing.beatPosition,
                        key: midiValue($0),
                        velocity: midiValue(voicing.velocity),
                        durationInBeats: voicing.durationInBeats
                    )
                }
            }
        }
    }

    /// Clamps to the MIDI 0...127 range. The generator should stay well inside it, but `midiNote`
    /// and `velocity` are free `Int`s on the wire and an out-of-range value would trap the
    /// `UInt32` conversion in the sequencer.
    private static func midiValue(_ raw: Int) -> UInt8 {
        UInt8(min(127, max(0, raw)))
    }
}
