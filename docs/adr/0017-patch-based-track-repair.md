# 0017. Patch-based track repair over full-track regeneration

Status: Accepted

## Context

The validate/repair loop (ADR 0013's bounded retry model) needs a repair step that takes a
`CandidateTrack` plus its `ValidationFailure`s and produces a corrected track. The first
implementation had the LLM regenerate the entire track from scratch on every retry attempt —
the same shape as the initial `GenerateStep` call, re-emitting every drum/bass/chord-voicing
note event each time.

This was truncating under `MaxOutputTokens` for tracks with enough notes or failures: the LLM
had to re-emit the whole track on every attempt even when only a handful of notes actually
failed validation, so repair cost scaled with total track size rather than with how much was
actually wrong.

## Decision

`IRepairStep`/`RepairStep` has the LLM produce a `TrackRepairPatch` instead of a replacement
track — a list of field-level `NoteCorrection` diffs (each referencing the original `NoteEvent`
plus only the changed fields: `BeatPosition`, `DurationInBeats`, `MidiNote(s)`, `Velocity`,
tagged with the `ValidationFailureType` that triggered it), plus optional whole-part replacement
(`NewDrumPart`/`NewBassPart`/`NewChordVoicingPart`) and `NewLengthInBeats` for failures that
can't be expressed as per-note corrections.

`RepairStep.ApplyPatch` merges this onto the previous `CandidateTrack`: whole-part replacement
establishes the base list first, then note corrections are grouped by their `Original` note
reference and merged onto it via a `with`-expression fold, so multiple corrections targeting the
same note combine into one update instead of the last one silently clobbering the others. A
correction whose `Original` doesn't match any note in the (possibly-replaced) part is dropped
with a logged warning rather than applied or thrown.

Patches are proportional to what's actually wrong, not to total track size — repair attempts now
scale with failure count instead of track length, directly addressing the truncation problem.

Getting there surfaced three real bugs, each informative about the design's actual failure
modes rather than incidental:
- `(List<DrumNoteEvent>)originalTrack.Drums` threw `InvalidCastException` — a
  collection-expression-typed `IReadOnlyList<T>` isn't backed by `List<T>`; fixed with `.ToList()`.
- Serializing a `DrumNoteEvent`/`BassNoteEvent`/`ChordVoicingEvent` through a `NoteEvent`-typed
  reference silently dropped `MidiNote`/`MidiNotes` entirely — `NoteEvent` needed
  `[JsonPolymorphic]`/`[JsonDerivedType]` on the base type itself; putting the derived-type
  attributes on the `JsonSerializerContext` instead (the first fix attempted) did nothing.
- The multi-correction merge bug described above: an earlier group-by-original-last-write-wins
  version silently discarded all but the last correction to the same note.

## Alternatives considered

- **Keep full-track regeneration, raise `MaxOutputTokens` instead.** Rejected: doesn't fix the
  underlying scaling problem (cost still grows with track size, not failure count), and the
  ceiling would eventually be hit again by a larger track or time signature.
- **Group-by-original, last-write-wins merge** (apply each `NoteCorrection` for a note by
  replacing the accumulator entirely rather than folding field-by-field). Rejected after it
  produced the multi-correction bug above — two corrections to the same note (e.g. one fixing
  beat position, one fixing duration) resulted in only the second correction's field actually
  landing, silently dropping the first.

## Consequences

- The repair prompt (`RepairStepPrompts.cs`) has to teach the LLM the patch vocabulary
  (`NoteCorrection` vs whole-part replacement vs length change) rather than just "here's a
  track, fix it" — a real prompt-engineering cost, and the prompt has needed iteration
  ("getting stuck sometimes") to keep the model reliably choosing the right correction shape.
- A correction that doesn't match any existing note is silently dropped (logged, not surfaced
  as a failure) — if the LLM consistently mis-references notes for a given failure type, that
  failure would keep resurfacing across attempts without an obvious signal why, until someone
  reads the warning logs.
- `ApplyPatch` now has three near-identical merge blocks (drum/bass/chord-voicing), one per
  note type — a real coupling to the closed set of note types; adding a fourth part (e.g. a
  melody/lead line) needs a fourth copy-pasted block with no compiler-enforced completeness
  check, flagged separately as a reuse follow-up rather than blocking this decision.
