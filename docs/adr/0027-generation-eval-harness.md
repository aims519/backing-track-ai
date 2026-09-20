# 0027. End-to-end eval harness as an in-process console app

Status: Accepted

## Context

Precise-mode generation is an agentic, LLM-backed workflow (retrieve → generate → validate →
repair → persist, with a planned rule-based fallback). Unit tests pin the deterministic pieces —
the validator, the chord parser, the repair-patch application — but they cannot answer the
question that actually matters after a prompt or model change: *does the pipeline still produce
valid tracks, and at what cost in repair attempts?*

We want a repeatable way to measure pipeline behaviour against a fixed set of representative
requests: happy-path cases, cases that should exercise the repair loop, and cases that should
fall through to the deterministic fallback.

## Decision

Add `api/eval/BackingTrack.Generation.Eval`, a console app that:

- Loads a **golden set** from version-controlled `GoldenSet/*.jsonl` — one case per line, input
  plus expected terminal outcome and attempt bounds, reviewed in diffs like any other source.
- Runs each case through the **real in-process `Workflow`** via `GenerationWorkflowBuilder`, the
  same graph the API's background worker runs — not a reimplementation.
- Judges results with **deterministic, binary checks only** (outcome kind, attempt count).
  Subjective musical quality is explicitly out of scope for the harness.
- Categorises cases (`Baseline`, `ValidatorTriggering`, `Fallback`) and reports a **per-category**
  pass/fail/pending breakdown, so a regression points at a stage.
- Treats cases for unbuilt stages (currently the fallback generator) as **pending, not failing**,
  so the golden set can be authored ahead of the code.
- Emits a machine-readable JSON report (`--json`) as the raw material for run-over-run diffing.

The harness is a sibling of `tests/` and `benchmarks/`, wired into `BackingTrack.sln` under an
`eval` solution folder.

## Alternatives considered

- **Black-box over HTTP** (drive the running API via `/generate` + `/status`). Rejected as the
  primary mode: it needs a live server and real storage, gives no view of attempt counts or
  which executor failed, and couples the harness to deployment. The `IGenerationRunner` seam
  leaves this open as a second runner later (a true end-to-end smoke test).
- **xUnit `[Theory]` in the test project.** Rejected: these runs are slow, cost money, are
  non-deterministic, and must not gate every `dotnet test`. A separate executable with its own
  exit codes keeps them opt-in and lets CI schedule them deliberately.
- **A managed eval product / third-party harness.** Rejected for a single-developer portfolio
  project — another dependency and account for what a few hundred lines covers, and building it
  is itself part of the portfolio story.
- **Deterministic validator-triggering via fault injection now.** Deferred: the first cut uses
  known-hard inputs (odd meters, fast tempos) that *encourage* a first-attempt failure. A
  fault-injecting `IGenerationRunner` that fails the first N validations is the deterministic
  version, added when the loop's behaviour is worth pinning exactly.

## Consequences

- The pipeline's behaviour becomes measurable and diffable across prompt/model changes, which
  is the core AI-engineering claim of the project.
- Every run makes real LLM + embedding calls and costs tokens. Until record/replay of LLM
  responses exists, the harness is a **manual / nightly** job, not a per-PR CI gate. Adding
  replay cassettes is the obvious next step if CI wants it.
- The golden set is a maintained asset: it must grow from real failures and stay small enough
  that a full run is cheap.
- The `SucceedsViaFallback` outcome branch is dark code until the fallback generator lands —
  acceptable, and tracked as pending in the report so it stays visible.
- A future LLM critic (musical-quality rejection) would add a `CriticTriggering` category and a
  graded quality check alongside the binary ones — out of scope here, noted so the category
  scheme has room for it.

## Update — 2026-09-20

Both anticipated gaps above have landed:

- The rule-based fallback generator (PR #72) is implemented, so `Fallback` cases are judged like
  any other category now instead of being reported pending.
- The Theory Critic (PRs #59–#71) added a critic → regenerate → re-review loop distinct from the
  validator's repair loop. A `CriticTriggering` category was added, using the same binary,
  deterministic judging as the other categories (outcome + attempt bounds) — not a graded quality
  check. `GenerationSucceeded` now also carries `CriticAttempts` so a case can assert the critic
  loop ran, the same way `Attempts` asserts the validator loop ran. Grading the critic's own
  verdict quality stays out of scope for this harness — that's what `BackingTrack.Critic.Eval`
  (PR #60) is for.
- `CriticTriggering`'s first cut mirrored `ValidatorTriggering`: "musically risky but legal"
  inputs meant to *encourage* a genuine critic rejection. In practice every case passed on the
  first review regardless of input — partly because the eval harness's `appsettings.json` was
  missing a `Critic` section entirely, silently defaulting `PassingScoreThreshold`/`MaxAttempts`
  to `0`/`0` and making rejection *impossible* rather than merely unlikely (fixed alongside this).
  Even after fixing that, there was no cheap way to find inputs the model reliably judges badly.
  Rather than keep guessing at provocative inputs, `CriticTriggering` cases now set
  `ForceCriticRejections` on the `EvalCase` (harness-only, not part of the real API payload) and
  a `FaultInjectingCriticStep` decorator (`api/eval/.../Running/FaultInjectingCriticStep.cs`)
  fails that many reviews deterministically before handing off to the real critic. This is the
  same fault-injection idea already deferred above for `ValidatorTriggering`, arrived at
  independently for the critic loop — worth applying there too if that deferral is ever picked
  back up.
- `Fallback` had the identical problem: `fallback-dense-changes` relied on a dense, unusual-meter
  chord progression to exhaust the validator's repair budget, and passed cleanly on a real run
  instead. Picked up the same fault-injection pattern immediately rather than waiting for the
  deferred `ValidatorTriggering` version: `ForceValidationFailures` on the `EvalCase` and a
  `FaultInjectingValidateStep` decorator force that many validations to fail before handing off
  to the real validator, guaranteeing `ValidateCandidateTrackExecutor`'s exhaustion → fallback
  branch runs. Also fixed a latent bug this surfaced: the case's original `minAttempts: 3`
  expectation could never pass even when fallback triggered correctly, since
  `FallbackGenerateCandidateTrackExecutor` resets `ValidationAttempts` to `1` for the fallback
  track's own validation — `Attempts` (and `CriticAttempts`) on a fallback-completed track are
  always `1`/`0`, not a reflection of how many attempts preceded it.
