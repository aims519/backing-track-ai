# Generation eval harness

`BackingTrack.Generation.Eval` — a console app that runs the Precise-mode generation pipeline
end to end against a versioned golden set and reports whether it still behaves.

## Why this exists

The pipeline is agentic and LLM-backed, so unit tests can't tell you whether a prompt or model
change made generation better or worse. This harness is the regression net for the *behaviour* —
validation pass rate, repair attempts, fallback rate — not the code.

See [ADR 0027](../../docs/adr/0027-generation-eval-harness.md) for the design decisions.

## What it does

1. Loads the golden set from `GoldenSet/*.jsonl` (one `EvalCase` per line).
2. Validates every case's input the way the API's `/generate` request validation would
   (`GoldenSetInputValidator`) — the harness skips the endpoint, so without this a bad chord
   symbol is silently dropped in retrieval instead of rejected. Malformed input aborts the run.
3. Runs each case's input through the real in-process generation `Workflow` — the same one the
   API's background worker builds (`WorkflowGenerationRunner`).
4. Compares the terminal outcome and attempt count against the case's `expect` block
   (`CaseEvaluator`).
5. Prints a per-category summary and a per-case table (verdict, attempts, elapsed, and the
   persisted `trackId` — inspect it at `<TrackStorage:BaseFolderPath>/<trackId>.json`). With
   `--json`, writes the same as a machine-readable report for run-over-run comparison.

Exit code: `0` pass, `1` one or more cases failed, `2` bad usage / no cases matched /
malformed golden-set input.

## Golden set categories

| Category              | Exercises                                             | Status |
|-----------------------|------------------------------------------------------|--------|
| `Baseline`            | generate → validate, first attempt, no repair        | live |
| `ValidatorTriggering` | validate → repair → re-validate loop                  | live |
| `Fallback`            | deterministic rule-based fallback generator           | pending — reported, not run |

`pending` cases are authored now so the golden set is ready when the stage lands. `CaseEvaluator`
flips them to live via the `FallbackGeneratorImplemented` constant.

A `CriticTriggering` category (inputs that validate but are musically poor, for the planned LLM
critic) will be added when that step exists — see the critic-step plan.

## Running it

```bash
# config: same secrets as the API + the KB embedder
cd eval/BackingTrack.Generation.Eval
dotnet user-secrets set "OpenAi:ApiKey" "<key>"
dotnet user-secrets set "VoyageAi:ApiKey" "<key>"

dotnet run -- --list                       # list cases + run input validation, no model calls
dotnet run -- --category Baseline           # run one category
dotnet run -- --case baseline-blues-a       # run one case
dotnet run -- --json ./eval-output/run.json # full run + machine-readable report
dotnet run -- -v                            # show the pipeline's own logs
```

Model / token settings are in `appsettings.json` (`OpenAi:*`, `TrackGenerationWorkflow:GenerationMaxAttempts`).

## Adding a case

Distil it from a real failure. Give it a stable `id` that's never reused, and put the "why" in
`rationale`. Keep the set small and representative — every case is an LLM call per run.

## Known gaps / next steps

- `ValidatorTriggering` cases currently *encourage* a first-attempt failure through hard inputs
  (odd meters, fast tempos) rather than *guaranteeing* one. Deterministic version: a fault-
  injecting `IGenerationRunner` / validator double that fails the first N attempts. See ADR 0027.
- No record/replay of LLM responses yet, so every run is live and costs tokens — fine for a
  manual/nightly job, not for per-PR CI. Replay vs. live-nightly is still to be decided.
- No run-over-run diffing yet; `--json` output is the raw material for it.
- Fallback stage not implemented (see table above); critic step and its category not built yet.
