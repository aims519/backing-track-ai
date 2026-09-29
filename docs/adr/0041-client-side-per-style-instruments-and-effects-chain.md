# 0041. Client-side per-style instrument mapping and a fixed effects chain

Status: Accepted

## Context

[ADR 0026](0026-bundled-soundfont-and-idempotent-audio-setup.md) bundled GeneralUser GS and
gave each voice (bass, drums, chords) its own `AVAudioUnitSampler`. Every track then played
on the same three presets (Finger Bass, Standard kit, Electric Grand Piano) through a dry
chain: sampler → mixer → output. A jazz ballad and a funk vamp sounded like the same General
MIDI demo. The scope doc asks for realism (`docs/backing-track-generator-scope.md`).

The form already makes the user pick from six presets (`MusicStyle`: blues, rock, pop, funk,
reggae, jazz), and the API's style knowledge base (`styles.jsonl`) has an entry for each. The
player never sees the enum, though. The style arrives only as the composed style string
(`"Blues - slow, brushes"`) carried as the track title.

The generated tracks carry notes only, with no instrument information. The CandidateTrack
contract, the files stored on the server ([ADR 0014](0014-local-filesystem-track-storage.md))
and the tracks already downloaded all have that shape.

## Decision

### 1. The app maps style → instruments (`InstrumentSet`)

`InstrumentSet.forStyle(_:)` (`app/BackingTrack/Audio/InstrumentSet.swift`) returns a bass
program, a drum kit and a chord program for each `MusicStyle`. `AudioEngine.setUp()` loads
them in place of the old hard-coded numbers. The numbers are 0-indexed GM programs, and every
one was checked against the presets in the bundled `.sf2`. Drum kits come from the
percussion bank (128).

- A `nil` style (a stored style string that doesn't parse to a preset) gets the original
  sound, so no track stops playing.
- The switch has no `default`. A new `MusicStyle` case won't compile until it has instruments.
- Chord instruments are all keyboards (electric piano, electric grand, clavinet, organ). The
  generator writes block voicings, and on a guitar preset every note of the chord is struck
  at once, which sounds wrong.
- Presets were chosen by ear on device. The Power kit and Rock Organ were rejected as too
  aggressive, and the acoustic grands didn't sit well in the mix.

The style gets to the player by parsing the title with `decomposeStyle`, which moved out of
`GenerateTrackViewModel` so it sits beside `MusicStyle`. `PlayableTrack` gains a
`style: MusicStyle?`. This needs no change to the model, persistence or navigation.

### 2. The style knowledge base describes instrumentation as idiom

Each `styles.jsonl` entry now says which instrument typically plays the keys and how the
chords and bass are articulated. For example, funk: "Keys are typically clavinet: very short,
syncopated 16th-note chord stabs…". The text never names preset numbers. This keeps the
generator writing parts that suit the instrument the app will play them on (stabs for a
clavinet, comping for an electric piano), while the actual program choice stays in one place.

A before/after eval run found no regressions. Blues and jazz chords shortened into comping as
intended. A first pop draft asking for "held" bass notes produced a whole-bar bass, so it was
reworded.

### 3. A fixed per-voice effects chain

```
bass   sampler ──────────► reverb ─► mixer ─┐
drums  sampler ──────────► reverb ─► mixer ─┼─► main mixer ─► peak limiter ─► output
chords sampler ─► EQ (HP) ─► reverb ─► mixer ─┘
```

- **Reverb:** a separate `AVAudioUnitReverb` inline on each voice, using the medium room
  preset. Each voice has its own wet level.
- **High-pass EQ on chords only, at 140 Hz:** keeps the keyboard's left hand clear of the
  bass. It sits before the reverb so the reverb tail carries no low end.
- **Pan:** chords are panned off-centre (−0.4) while drums and bass stay centred. It is set on
  each voice mixer after that mixer is connected to the main mixer, because pan belongs to that
  connection.
- **Peak limiter:** Apple's `kAudioUnitSubType_PeakLimiter` sits between the main mixer and the
  output to stop the three summed voices clipping. The automatic main-mixer → output connection
  is broken first so the limiter can go in between.

All of this is built once in the idempotent `setUp()` from ADR 0026. The same settings apply
to every style.

## Alternatives considered

**The API returns instrument choices with the track.** The LLM could then choose instruments
to match the notes it wrote, or vary them from track to track. Rejected for now:

- It changes the CandidateTrack contract. Tracks already stored and downloaded have no
  instrument field, so the app would need a fallback mapping anyway.
- It ties the server to one client's SoundFont program numbers. A different bank, or the
  generative-audio Vibe mode, would make them meaningless.
- The presets changed three times in one tuning session. That kind of change is cheap on the
  client and would need a deploy on the server.

Worth revisiting if instrument choice should depend on more than the style: on the notes
themselves, or on the user's free-text style notes.

**Preset numbers in the generation prompt.** This would keep the generator in step with the
exact instruments, but the mapping would then exist twice and the copies would drift apart.
Describing instrumentation as style idiom gets the articulation benefit without copying the
mapping.

**User-selectable instruments.** More control, but it needs picker UI and persisted choices.
Deferred. Style-driven defaults come first.

**One shared reverb on a send bus** (`AVAudioConnectionPoint` fan-out to a single reverb). This
is closer to a real room, since all voices share the same space, and it uses one reverb unit
instead of three. It needs more wiring and gives up simple per-voice wet levels. The inline
version was chosen for simplicity. Revisit if the voices sound like they're in different rooms.

**Better or per-instrument samples before any mix work.** Deferred. GeneralUser GS is decent
for its size. Most of the "GM demo" sound came from the dry, unpanned mix and the identical
instruments, which cost nothing to fix. Better sample banks cost app size and need their
licences checked (ADR 0026).

## Consequences

- **Two places must agree.** `InstrumentSet` and the instrumentation text in `styles.jsonl`
  describe the same choices. Changing a chord instrument (organ to piano, say) should prompt a
  check of the style text, and every edit to that text needs a re-embed and an eval run. They
  can drift without anything failing.
- **Playback depends on the style string's format.** If `composedStyle`'s `"Style - notes"`
  format changes, `decomposeStyle` returns `nil` and every track quietly falls back to the
  default instruments. The round-trip tests in `GenerateTrackViewModelTests` guard against
  this.
- **Levels aren't balanced per preset.** Presets differ in loudness, and there is no per-style
  gain yet. The limiter stops the mix clipping, but it can also hide a preset that is simply
  too hot.
- **Effect settings are global.** A jazz ballad and a rock track get the same reverb and pan.
  Per-style effects would mean adding them to `InstrumentSet` (or a sibling type).
- **Instruments are fixed at engine setup.** Changing the style means building a new
  `AudioEngine`. That's fine today, because a track has exactly one style.
- **Tuning happens on device.** The simulator's sampler behaves differently (voice allocation,
  see `PlayerSequenceBuilder.merging`), so preset and effect choices need to be heard on a
  phone.
- **No tests pin the mapping itself.** It's a lookup table whose correctness is judged by ear.
  The exhaustive switch is the only compile-time guard.
