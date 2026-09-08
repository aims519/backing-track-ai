# 0026. Bundled SoundFont and idempotent audio-engine setup

Status: Accepted

## Context

The player screen has to turn a downloaded `CandidateTrack` — MIDI-style note events in
beat time, no audio ([ADR 0014](0014-local-filesystem-track-storage.md)) — into audible
sampled instruments, looping, with the tempo adjustable live on-device
(`docs/backing-track-generator-scope.md`: AVFoundation, "sampled instruments (not basic
synth tones)", "tempo-adjustable and loopable live").

iOS supplies the pieces: `AVAudioUnitSampler` (a sampler fed MIDI events) and
`AVAudioSequencer` (plays `AVMusicTrack`s against a tempo track), both hanging off an
`AVAudioEngine` graph that needs an active `AVAudioSession`.

A proof-of-concept (`app/BackingTrack/Audio/AudioEngine.swift`) now plays a single bass
note through that chain. Building it forced two decisions this ADR records: **which sound
source** the sampler plays, and **how the engine/sampler/sequencer lifecycle is
structured**. The choice of `AVAudioSequencer` over a hand-written scheduler is noted
below but not yet locked.

## Decision

### 1. Bundle GeneralUser GS as a `.sf2`; don't rely on the built-in sound set

`AVAudioUnitSampler` falls back to the OS DLS soundbank (Roland GS) when it's only sent
program changes — zero assets. Its drum kit and electric bass are thin, and the scope doc
explicitly wants realism, so instead:

- **GeneralUser GS** (S. Christian Collins), ~31 MB, committed at
  `app/BackingTrack/Resources/GeneralUser-GS.sf2`. Its readme grants free use in any
  private or commercial project, no royalties, no in-app attribution required — so
  bundling it in the app carries no obligation.
- **`.sf2`, not `.sf3`.** `.sf3` only buys smaller files (Ogg-compressed samples), which
  isn't the lever here — trimming unused presets is — and `AVAudioUnitSampler` support for
  it is undocumented and not to be relied on. No reason to consider it.
- Instruments are selected with `loadSoundBankInstrument(at:program:bankMSB:bankLSB:)`
  using the AUSampler bank constants (`kAUSampler_DefaultMelodicBankMSB` `0x79`,
  `kAUSampler_DefaultPercussionBankMSB` `0x78`), which map to the file's GM banks, rather
  than raw bank-number splits.
- One `AVAudioUnitSampler` per voice (bass / chords / drums), each loading its own
  instrument from the same bundled file.

This **reverses** the initial plan's "built-in GM for v1, bundle a SoundFont later" —
the swap point (a `loadSoundBankInstrument` call, a binary asset, a license to vet) is
cheap to get right now and fiddly to retrofit, and the built-in sound was judged not
good enough to ship even as a first cut.

### 2. Idempotent, create-once setup behind an `isSetUp` guard

`AudioEngine.setUp(tempoBpm:)` is guarded by an `isSetUp` flag and is safe to call
repeatedly; `play(track:)` — the only public entry point — calls it unconditionally every
time. The caller never sequences "configure → activate session → start → play" or tracks
whether the graph is already up; the screen can be entered, left, re-entered and Play
tapped many times without the engine being rebuilt.

What must happen exactly once, and why repeating it is wrong or wasteful:

- `engine.attach(sampler)` / `engine.connect(...)` — attaching an already-attached node or
  rebuilding connections on a running engine throws or drops audio.
- `AVAudioSession.setActive(true)` — cheap but not free; toggling it repeatedly risks a
  glitch and stepping on in-flight setup.
- `loadSoundBankInstrument` — a disk read plus parse of a 31 MB bank; tens of milliseconds,
  not per-play work.
- `AVAudioSequencer(audioEngine:)` + `createAndAppendTrack()` + note events — recreating
  the sequencer orphans tracks bound via `destinationAudioUnit`; appending tracks again
  doubles every note.

`isSetUp` flips to `true` only after the whole block succeeds, so a failed setup (missing
file, session denied) is retried on the next `play` rather than wedging a half-built
engine. `play` separately re-checks `engine.isRunning` and restarts if needed: the session
can be stopped out from under the app (interruption, backgrounding) without `isSetUp`
becoming false, so "graph built" and "engine running" are deliberately tracked apart.

## Alternatives considered

**Built-in GM sound set, no bundled file.** Rejected. The OS DLS drum kit in particular
is weak, and the scope doc calls for sampled realism. Deferring meant shipping the thin
sound and swapping later; the swap is cheap now (one API call differs) and the ~31 MB cost
was judged acceptable.

**A trimmed SoundFont.** GeneralUser GS can be cut in Polyphone to just the presets used
(one bass, one keyboard, one drum kit), dropping the asset to a few MB. Deferred until the
instrument choices are final — noted as a follow-up, not a blocker.

**`AVAudioSequencer` vs a hand-rolled beat-clock scheduler.** The sequencer was taken for
the POC: it gives a tempo track, sample-accurate note timing and `AVMusicTrack` looping
for free, versus a `DispatchSourceTimer` pumping `startNote`/`stopNote` by hand. Its known
weak spot is live tempo change — editing `AVExtendedTempoEvent`s on a running sequence's
tempo track is awkward, where a custom clock re-bases trivially. Not locked here: if live
tempo proves unworkable on the sequencer, the scheduler is the fallback and the
idempotent-setup structure below carries over unchanged.

**Explicit lifecycle API (`configure()` / `teardown()`) instead of an idempotent `play`.**
Rejected for now. More surface to misuse — call `play` before `configure`, forget
`teardown` — for a screen that has one meaningful state, "ready to play". A `teardown()`
on dismissal (to release the session so other apps' audio resumes) is still likely, but
that doesn't require the *setup* path to be non-idempotent.

## Consequences

- The repo and every app build carry a 31 MB binary. It isn't `git-lfs`-tracked (the repo
  has no LFS today); revisit if more large assets land. A trimmed bank is the obvious size
  win.
- **Provenance:** the GeneralUser GS readme grants free private/commercial use with no
  obligations, so there's nothing to comply with — but committing that readme (or a note
  pointing to its source and version) next to the `.sf2` keeps the asset's origin
  auditable in the repo, the way generated and third-party assets are elsewhere
  ([ADR 0021](0021-code-drawn-visual-assets.md)). `Resources/` currently has only its own
  `README.md`.
- `AudioEngine` holds process-wide audio resources for its owner's lifetime. Nothing tears
  the engine down or deactivates the session yet; a dismissal `teardown()` is still owed
  (and would flip `isSetUp` back).
- `isSetUp` guards *structure*, not *content*. The POC's `setUp` also bakes in the tempo
  and one hard-coded note from the first call. Playing a different track, or changing
  tempo, isn't expressed yet — turning `setUp` into "build the graph once, then load track
  N" is the next step.
- The single-`destinationAudioUnit` wiring is per-track. The real arrangement (separate
  drums/bass/chords tracks → separate samplers) multiplies the create-once cost but keeps
  the same idempotency structure.
- `setUp` currently logs and swallows its errors; `play` then silently no-ops on
  `guard let sequencer`. The caller gets no signal that audio failed — fine for a POC, but
  the real screen needs the failure surfaced so the UI can show "couldn't start playback".
