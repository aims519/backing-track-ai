# 0033. Critic assumes pitch legality rather than re-checking it

Status: Accepted

## Context

The Theory Critic's `ChordToneAdherence` dimension asked the LLM judge to verify that every note
in a bar was within that bar's allowed chord-plus-matched-scale tone set. In practice this was
unreliable: eval fixtures showed the critic confidently rejecting genuine matched-scale color
tones (a Dorian raised 6th, a 9th) as "outside the chord," even after the prompt was updated to
mention `MatchedScales` explicitly and to hand it pre-resolved note names (`CriticMatchedScale`)
instead of raw intervals it had to transpose itself. In several runs the model's own free-text
reasoning correctly concluded a note *was* allowed, then still submitted `Pass: false` for the
dimension — the structured verdict didn't track its own stated reasoning.

Separately, `GenerationWorkflowBuilder.cs`'s workflow graph only sends a candidate track to
`CriticExecutor` after `ValidateCandidateTrackExecutor` reports `IsValid`
(`AddEdge(validate, critic)`, gated on `trackValidationResult.IsValid`). `TrackValidator`'s
`ValidateBassNoteChoices`/`ValidateChordVoicingNoteChoices` already deterministically guarantee,
for every note in every bar, that it's within the allowed chord/scale tone set — before the
critic ever sees the track. The critic re-deriving that same fact in prose was therefore not just
unreliable, it was redundant with a check the code had already proven true.

## Decision

`ChordToneAdherenceInstructions` now states pitch legality as a given fact ("every note ... has
already been deterministically validated ... a note that was actually outside that set cannot
occur here") and instructs the critic never to fail the dimension on that basis. The dimension is
rescoped to judge only root audibility — can the listener identify each bar's chord root from the
cooperation between bass and chord voicings — which is the part that's genuinely a matter of
musical judgment, not arithmetic.

`CriticMatchedScale` (mirroring the existing `CriticCandidateTrack` wrapper) is kept: it still
resolves each `MatchedScale` to real note names for the chord it's tied to, so that context
remains available to the critic as harmonic background (useful for `VoiceLeading` and for citing
knowledge-base items) without inviting it to re-litigate tone membership.

## Alternatives considered

- **Keep iterating the prompt wording** (explicitly requiring the critic to name violating notes
  and cross-check them against the matched scale before failing). Tried across several rounds.
  Each iteration fixed the specific failure the previous eval run exposed but shifted the
  unreliability elsewhere — tightening the per-chord scope caused a previously-reliable
  true-positive fixture to start passing incorrectly; re-running the *identical* reverted prompt
  text later produced a materially different result profile, indicating the sample sizes involved
  (3-5 repeats) couldn't reliably distinguish a real prompt effect from the critic model's
  inherent run-to-run variance. Rejected as a dead end for this specific failure once that noise
  floor became apparent.

- **Add an explicit per-bar tone-check field to the `CriticVerdictSubmission` tool schema**,
  forcing the model to show its work in a structured field before committing to `Pass`. Considered
  as the next step after prompt iteration stalled, but superseded once the workflow-graph
  invariant was noticed: since the check this field would force is already guaranteed by
  `ValidateStep` before the critic runs, having the LLM redo it in a structured field is still
  redundant work carrying the same arithmetic-reliability risk, just relocated from prose to JSON.
  Removing the check entirely is strictly better than making the model perform it more visibly.

## Consequences

- Eliminates the specific false-positive class this session traced (legitimate scale-tone color
  notes rejected as "outside the chord"), confirmed via the critic eval harness's
  `good-modal-color-tones-from-matched-scale` fixture.
- `flawed-chord-tones-off-by-a-semitone`, the eval fixture that intentionally fed deliberately
  wrong pitches straight to `ICriticStep` (bypassing `ValidateStep`, per ADR 0030's separate
  code-based critic eval), is retired — asserting the critic must still catch that would mean
  testing behavior this change explicitly instructs it not to perform. Replaced with
  `flawed-root-never-established`, which isolates the dimension's real remaining job: pitch-legal
  voicings that never sound the root.
- **Couples `ChordToneAdherenceInstructions`'s correctness to the `validate -> critic` workflow
  edge.** If the pipeline ever routes an unvalidated or partially-validated track to the critic
  (a fast-path, a different mode, a future workflow change), this instruction's "cannot occur
  here" claim becomes false with no compiler or test to catch the mismatch — anyone changing that
  edge needs to know this prompt depends on it.
- Does not address the Theory Critic's broader run-to-run inconsistency observed on other
  dimensions (`VoiceLeading` flagged a couple of otherwise-good fixtures in adjacent eval runs,
  unrelated to this change) — left as a pre-existing, unresolved characteristic of the current
  single-shot (no repeated-sampling/voting) critic design.
