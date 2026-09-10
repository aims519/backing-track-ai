# 0028. SilentBar repair via whole-part replacement, not a surgical bar-fill

Status: Proposed

## Context

`GenerateStep` now derives `LengthInBeats` from the request rather than the model (see the
"compute LengthInBeats in code" change). A side effect: a candidate that under-fills the track —
writes 8 bars of material for a 16-bar request — no longer surfaces as a length mismatch. The
track is the right length; some bars in the middle or tail just have no events.

A new deterministic validation rule, `TrackValidator.ValidateSilentBars`, flags any bar that
contains zero events in a given part (drums, bass, or chord voicings) and emits a `SilentBar`
failure per empty part per bar. The `NoteEvent` on the failure is null — the problem is absence,
not a specific bad note. The repair loop needs a mechanism to fix it.

## Decision

`SilentBar` failures route to the existing **whole-part replacement** mechanism — the same one
`PartEmpty` uses. The repair agent regenerates the entire affected part via
`NewDrumPart` / `NewBassPart` / `NewChordVoicingPart`, carrying the existing bars' events across
unchanged and adding events for the empty ones. One replacement per part covers all of that
part's `SilentBar` failures, and note corrections targeting a replaced part are dropped when
applying the patch.

`ValidateSilentBars` runs on every candidate, alongside the existing checks in `ValidateStep`.

## Alternatives considered

- **Surgical `BarFill` — an additive patch entry supplying only the events for the missing
  bars.** Rejected for the first cut. It needs a new field on `TrackRepairPatch`, a new prompt
  section, new insert-and-resort logic in `ApplyPatch`, and its own tests — added to a repair
  loop that is already the most fragile part of the pipeline. A silent bar usually means the
  part is structurally wrong (the model lost the bar count), not that one bar needs a targeted
  insert, so regenerating the whole part matches the failure mode. Kept as the fallback if eval
  data shows whole-part replacement is slow or regression-prone on a capable model.

- **Fail fast to the rule-based fallback generator on any silent bar.** Rejected. A silent bar
  is often recoverable in a single repair pass — an eval run went from 8 empty bars to a valid
  16-bar track in one replacement — and the fallback generator does not exist yet. Reserving it
  for genuine repair exhaustion keeps the agentic path doing its job.

- **Collapse `PartEmpty` into `SilentBar`** (a wholly empty part is just every bar silent). Not
  done. `ValidateNoPartEmpty` stays as a cheap, distinct check; the overlap — both fire, both
  route to whole-part replacement — is harmless.

## Consequences

- Reuses a mechanism that is already implemented and tested; no new patch surface.
- A `SilentBar` repair regenerates the good bars too, so the whole part is re-validated and can
  bring new note-choice failures, extending the loop by an attempt. Acceptable for something
  that should be uncommon.
- **Model-sensitive.** Eval runs on a mini model showed it cannot execute "replace the whole
  part with events in every bar" reliably: it fills the empty bars but empties others, and the
  repair loop oscillates until it exhausts its attempts. The full model handles it in one pass.
  This rules out a mini model for the repair step, and is the data point that would justify the
  surgical `BarFill` alternative if a capable model ever regresses here.
- The eval harness gains a `validator-silent-bar-long-vamp` golden case (a 16-bar repetitive
  vamp — a probabilistic trigger, since input alone cannot force the model to under-fill) and
  `trackId` in its console and JSON output for inspecting the persisted result.
