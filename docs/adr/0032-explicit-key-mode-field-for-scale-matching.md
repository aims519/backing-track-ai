# 0032. Explicit `KeyMode` field, song-wide, over inferring mode or per-chord scale matching

Status: Accepted

## Context

`ScaleMatcher.FindMatchingScalesForChords` computed a track's "diatonic tones" — the pitch-class
set a candidate scale must stay inside to be considered a valid match for a chord — by always
transposing the hardcoded major-scale intervals onto the job's `KeyRoot`. There was no concept of
minor or modal keys anywhere in the pipeline.

This surfaced as a real production bug (job `afceac62-9df3-41ae-850e-f47b8cecd615`): a minor-key
jazz progression (`KeyRoot: "A"`, chords `Amin7 / Dmin7 / Emin7 / Amin7`) was validated against A
**major**'s diatonic set instead of A minor's. Several of its own chords' tones didn't even fit
that set, so `FindMatchingScalesForChords` returned zero matched scales for those bars, and
`TrackValidator` fell back to bare chord-tones-only validation — rejecting legitimate walking-bass
passing tones and voicing extensions, and causing the repair loop to flatten the line back to
plain triads.

Neither the API request DTO nor the iOS form captured anything about mode — a "key" was just a
root note, with mode left for the generation LLM to infer from the chord list (which it did
correctly; only the deterministic validator got it wrong).

## Decision

Add a `KeyMode` enum — the seven church modes (`Ionian, Dorian, Phrygian, Lydian, Mixolydian,
Aeolian, Locrian`) — as a required field alongside `KeyRoot`, threaded through
`GenerationRequestDto` → `GenerationJob` → `ScaleMatcher.FindMatchingScalesForChords` →
`RetrieveStep`/`RetrieveScalesExecutor`, and mirrored on the iOS side (`GenerateTrackViewModel`,
`GenerationRequest`, `GenerationJob`, `JobHistoryEntry`). `ScaleMatcher` looks up the declared
mode's own interval set (a `Dictionary<KeyMode, int[]>`) instead of always assuming major.

`KeyMode` defaults to `Ionian` when omitted from a request (validated the same way `Mode` is), so
existing clients that don't send it keep working unchanged.

Alongside this, `ScaleMatcher.GetChordsOutsideKey` adds a **non-blocking** warning
(`GenerationJob.KeyModeWarnings`) when a chord's own tones don't fit the declared key/mode —
advisory only, since a mismatch may be intentional (a borrowed chord, modal interchange), and
generation proceeds regardless.

## Alternatives considered

- **Infer the mode from the chord progression** (a best-fit scorer comparing each chord's tones
  against every mode's diatonic set rooted on `KeyRoot`, picking the best match). Rejected: it's a
  heuristic that can misfire on exactly the progressions this fix targets — secondary dominants,
  borrowed chords, or a progression that never states its own tonic chord — trading a predictable
  bug for an occasional, harder-to-diagnose one. It also wouldn't have saved any UI work: the iOS
  form had no mode concept to reuse either, so there was no already-known value being thrown away
  by asking the user explicitly.

- **Go straight to per-chord scale matching** — drop the song-wide diatonic constraint entirely,
  matching each chord only against its own tones (dropping the second `IsScaleValidForChord`
  check in `FindMatchingScalesForChords`). This is structurally the more complete fix for jazz
  harmony (a V7 chord could bring in Mixolydian/altered regardless of the home key) and was
  discussed as the likely eventual destination. Rejected for this change: it's a broader semantic
  shift — "valid" would mean something looser everywhere it's checked (the validator, the Theory
  Critic's scoring, the repair loop's corrections) — and needs the eval harness to confirm it's
  more *correct*, not just more permissive, before landing. Scoped as a separate follow-up.

- **Major/minor only, not the full seven modes.** Rejected: jazz specifically leans on modal
  harmony beyond major/minor (Dorian vamps, Mixolydian dominant pedals), and modeling all seven
  church modes costs the same amount of code as just two (one interval-lookup table either way).
  Doing it now avoids a second migration through the DTO/job/UI chain later.

## Consequences

- Fixes the reproduced bug: a minor-key or modal progression is now validated against the correct
  diatonic set. The regression case (`baseline-minor-key-walking-bass` in the generation eval
  harness, moved there from `ValidatorTriggering` since it no longer needs repair) went from
  failing a `maxAttempts: 1` bound to passing in exactly one attempt.
- Does **not** fully unlock jazz harmony on its own: a chord that calls for a different parent
  scale than the one declared song-wide mode (a secondary dominant, an altered dominant, modal
  interchange) can still get zero matched scales and fall back to the same chord-tones-only
  validation this fix was meant to eliminate — confirmed by `GetChordsOutsideKey`'s existence as a
  warning rather than a hard block. Per-chord matching remains the candidate follow-up for that
  gap.
- Also doesn't touch the Theory Critic (LLM judge): its `ChordToneAdherence` prompt only mentions
  "allowed chord tones," not the `MatchedScales` tones it's also given as input, so it can still
  flag legitimate modal color tones as violations even when the deterministic validator now
  accepts them (observed directly on a manually-tested Dorian jazz track). A separate follow-up.
- iOS: `JobHistoryEntry.keyMode` defaults to `"Ionian"` for the SwiftData lightweight migration —
  rows written before this field existed are treated as the previously-implicit major assumption.
  `KeyModeWarnings` is not persisted to history; it's transient, shown only on the progress screen
  right after a submission.
