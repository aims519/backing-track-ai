# Eval results

The purpose of these tests was to determine the best OpenAI model combination to use in the track generation workflow. There are three steps in the workflow that use LLM calls: Generate, Repair, and Critic. To determine the best fit, tests included:

- Can a track be successfully generated for golden set inputs?
- How much does the track cost to generate?
- Are appropriate fallback states reached when failures are forced for the critic or validation steps?
- Does the workflow correctly recover in a reasonable number of attempts when expected failures are encountered?
- Does the critic agent correctly identify problems in a generated track?

Tests were run multiple times to smooth out natural fluctuations as the outcomes are not deterministic.

- Harness: [`api/eval/BackingTrack.Generation.Eval`](../api/eval/BackingTrack.Generation.Eval)
- Design: [ADR 0027](adr/0027-generation-eval-harness.md)
- Model/tier split that motivated the comparison: [ADR 0036](adr/0036-per-step-model-tier-split-for-cost.md)

## Run details

| | |
|---|---|
| Run date | 2026-10-09 to 2026-10-10 (UTC; the `luna6-all` runs finished after midnight) |
| Commit | `c0ee09d` (the golden set was last changed in `d6a03a4`; `c0ee09d` only touched critic-eval fixtures, so the generation eval is unaffected) |
| Golden set | 13 cases: Baseline 6, ValidatorTriggering 3, CriticTriggering 2, Fallback 2 (9 organic, 4 with fault injection). |
| Runs per model | 3 for every configuration. Run 1 and run 2 of the first configuration each had one case (`validator-silent-bar-long-vamp`) hit a Voyage AI `429` before reaching any model, so it was rerun alone and that result used instead (`baseline-<n>.rerun-validator-silent-bar-long-vamp.json`) |
| Service tier | Flex for generate, repair and critic |
| Prices | USD per million tokens (input / output), standard tier, from `Pricing:Models` in the harness `appsettings.json`:<br><br>`gpt-5.6-terra` $2.00 / $12.00<br>`gpt-6.1-sol` $2.00 / $10.00<br>`gpt-5.6-luna` $0.20 / $1.20<br>`gpt-6-luna` $0.10 / $0.50<br><br>Flex is billed at 0.5× (`Pricing:ServiceTierMultipliers`).<br><br>Taken from [OpenAI models](https://developers.openai.com/api/docs/models) on 2026-10-09. |
| Generation / repair attempts budget | `TrackGenerationWorkflow:GenerationMaxAttempts` = 3 in the eval. The API's own setting is 5 in Production and 10 in Development, so production has a larger repair budget than was tested |
| Critic | `PassingScoreThreshold` = 100, `MaxAttempts` = 3 |
| Raw reports | All result JSON and text files can be found in [`docs/eval-results/2026-10-09/`](eval-results/2026-10-09/).<br><br>`full-set/` has one `--json` report per run of all 13 cases (`<config>-<n>.json`, config names as in `summarise.py`);<br><br>`critic/` has the critic eval's console output for the original fixtures, and `critic-revised-fixtures/` the second run's.<br><br>`full-set/baseline-<n>.rerun-<case>.json` is a rerun of one case that hit a rate limit, and replaces that case.<br><br>`TrackStorageDirectory` was removed from the copies (a local path), and the generated track files aren't included |

## Results by model

| Model config (generate / repair / critic) | Pass rate | Mean attempts | Mean critic attempts | Fallback rate | Mean (median) time per track | Mean cost per track |
|---|---|---|---|---|---|---|
| `gpt-5.6-terra` / `gpt-5.6-luna` / `gpt-5.6-luna` (Flex) | 38/39 (97%) | 1.19 | 1.46 | 1/27 (4%) | 40.5 s (31 s) | $0.0440 |
| `gpt-6.1-sol` / `gpt-5.6-luna` / `gpt-5.6-luna` (Flex) | 39/39 (100%) | 1.00 | 1.19 | 0/27 (0%) | 80.7 s (68 s) | $0.0306 |
| `gpt-6.1-sol` / `gpt-6-luna` / `gpt-6-luna` (Flex) | 39/39 (100%) | 1.00 | 1.15 | 0/27 (0%) | 100.0 s (74 s) | $0.0301 |
| `gpt-6-luna` / `gpt-6-luna` / `gpt-6-luna` (Flex) | 39/39 (100%) | 1.11 | 1.59 | 0/27 (0%) | 162.0 s (132 s) | $0.0029 |

The results above are consistent with the findings of ADR 0036, i.e. that using the cheaper Luna models (`gpt-6-luna` in this test) for the Generate step is not desirable. Although the same overall pass rate is achieved, the quality of the initially generated track means drafts are more often rejected at the critic step, which increases the overall generation time (1.59 vs 1.15 critic attempts, 162 s vs 100 s).

Using a more capable model for the first generation improves quality sufficiently so that drafts are rejected less often, which gives faster generation times. There is an acceptable cost overhead for this given the low volume of requests (approx. 10x more expensive: $0.0306 vs $0.0029 per track).

### Decisions

We'll opt to use the `gpt-6.1-sol` model for generation. Switching from Terra to Sol for the important generation step means fewer attempts and critic rejections, is cheaper, and had 0 fallbacks in the 27 case-runs, where Terra had 1. The downside is that Sol runs take - on average - 40 seconds longer, but this is acceptable as generation runs as a background task with push notifications on completion for the user.

For repair and critic steps, the results for `gpt-5.6-luna` vs `gpt-6-luna` with `gpt-6.1-sol` for generation are very similar. We'll opt for the `gpt-5.6-luna` model since the mean and median time per track is reduced, although not in a significant way. It is worth noting that the Flex timing is noisy in general. The critic eval, discussed in the next section, could not distinguish them.

## Critic results by model

Harness: [`api/eval/BackingTrack.Critic.Eval`](../api/eval/BackingTrack.Critic.Eval) · design:
[ADR 0030](adr/0030-critic-eval-as-a-separate-code-based-project.md)

The critic agent is evaluated separately because a different type of measure is needed. Instead of evaluating whether a track is successfully generated, we evaluate whether the critic comes to a reasonably expected conclusion based on fixed input.

If the critic model starts to produce unexpected rejections it will lead to failed tracks and lower quality output for the user (i.e. fallback, deterministic tracks). Similarly, if the critic is too lenient, poorly constructed tracks will slip through that are less useful for practice.

The fixture tracks are constructed with a known-correct verdict, repeated to measure
consistency. The critic eval reports no tokens or cost and has no `--json` report, so these numbers come from its console summary.

| | |
|---|---|
| Run date | 2026-10-09 |
| Commit | `609134c` for the original fixtures (first run); `c0ee09d` for the revised fixtures (second run). Both runs' console output predates the commit that holds the code they ran (they ran from uncommitted changes) |
| Fixtures | 6 in `Fixtures/CriticFixtures.cs`: 3 known-good (`good-basic-rock-comping`, `good-inverted-voice-leading`, `good-modal-color-tones-from-matched-scale`) and 3 flawed, one per dimension (`flawed-static-voicing-ignores-progression`, `flawed-root-never-established`, `flawed-metronomic-no-groove`). Two of the good fixtures were revoiced after the first run (see "Revised fixtures") |
| Repeats per fixture | 10 (`--repeat 10`) |
| Passing score threshold | `Critic:PassingScoreThreshold` = 100, matching the API and the generation eval. Earlier critic-eval runs (including those behind ADR 0036) may have used a different value, so don't compare their pass/fail counts directly. Every score in these runs is 33.3, 66.7 or 100, so a threshold of 80 or 100 gives the same verdicts |
| Service tier | Flex (`CriticServiceTier`) for the first run and for `gpt-5.6-luna` on the revised fixtures; standard for `gpt-6-luna` and `gpt-6.1-sol` on the revised fixtures (see below) |

### Original fixtures (first run)

Two of the three good fixtures turned out to contain real `VoiceLeading` defects, so the failures on them below are not all false failures. See the next subsection.

| Critic model | Fixtures correct | Inconsistent across repeats | Runs missing expected citation | Notes |
|---|---|---|---|---|
| `gpt-5.6-luna` | 3/6 | 3 | 1 | Failed the "good" tracks in 7 of 30 runs (23%), all citing `VoiceLeading` (one also cited `RhythmAndFeel`); passed a flawed track in 1 of 30 |
| `gpt-6-luna` | 4/6 | 2 | 0 | Failed the "good" tracks in 11 of 30 runs (37%), all citing `VoiceLeading`; never passed a flawed track |

Per fixture, runs that matched the expected verdict out of the repeats:

| Fixture | Expected | `gpt-5.6-luna` | `gpt-6-luna` |
|---|---|---|---|
| `good-basic-rock-comping` | pass | 7/10 | 7/10 |
| `good-inverted-voice-leading` | pass | 10/10 | 10/10 |
| `good-modal-color-tones-from-matched-scale` | pass | 6/10 | 2/10 |
| `flawed-static-voicing-ignores-progression` | fail (`VoiceLeading`) | 10/10 | 10/10 |
| `flawed-root-never-established` | fail (`ChordToneAdherence`) | 9/10 | 10/10 |
| `flawed-metronomic-no-groove` | fail (`RhythmAndFeel`) | 10/10 | 10/10 |

| Column | Source in the console summary |
|---|---|
| Fixtures correct | `N/M fixtures correct` on the last line |
| Inconsistent across repeats | `K inconsistent across repeats` on the last line, and the `[INCONSISTENT]` fixtures |
| Runs missing expected citation | Count of `NOTE: expected a citation on ...` lines |
| Per-fixture matches | The `runs:` line under each fixture (`pass=`/`fail=` per repeat) against its `expected` verdict |

### Revised fixtures (second run)

Reading the critics' citations showed that the first run's results were skewed by the fixtures. `good-modal-color-tones-from-matched-scale` moved four voices down a fifth in parallel from bar 1 to bar 2, and four voices up a step in parallel into bar 3. `good-basic-rock-comping` leaped G4 down to A3 into the Am chord and moved F to G as a parallel block. Both were revoiced to hold common tones and move the other voices by step, and the critic eval was rerun at 10 repeats.

| Critic model | Service tier | Fixtures correct | Inconsistent across repeats | Runs missing expected citation |
|---|---|---|---|---|
| `gpt-5.6-luna` | Flex | 6/6 | 0 | 0 |
| `gpt-6-luna` | Standard | 6/6 | 0 | 0 |
| `gpt-6.1-sol` | Standard | 6/6 | 0 | 0 |

All three models matched the expected verdict in 10 of 10 runs on every fixture, so these fixtures no longer separate them. On `flawed-root-never-established`, `gpt-6.1-sol` scored 33.3 (two dimensions cited) in 9 of 10 runs, `gpt-6-luna` in 4 of 10 and `gpt-5.6-luna` in 3 of 10; the verdicts were the same.

The first attempt at `gpt-6-luna` used the Flex tier. Individual calls stalled for about 5 minutes (one for about 15), so it was stopped after 16 of 60 runs and rerun, together with `gpt-6.1-sol`, on the standard tier, where no call took longer than 20 s. Verdicts shouldn't depend on the tier, but it is a difference between rows.

These results of the critic eval could not distinguish any of the models.

## Where each number comes from

| Column | Source | Caveat |
|---|---|---|
| Pass rate | `Passed / (Passed + Failed)` over every case in the `--json` report; Pending excluded | Cases `expect` a specific outcome, so a `Fallback`-category case "passing" means it fell back as designed |
| Mean attempts | `Cases[].Attempts`, over organic cases that didn't end in fallback | Always `1` on a case that completed via fallback, so those are excluded (see eval README, known gaps) |
| Mean critic attempts | `Cases[].CriticAttempts`, over organic cases that didn't end in fallback | 1 = the critic accepted the first candidate, 2+ = it rejected at least once first (0 = fallback resets the critic count, so these cases are excluded). A strict critic shows up here and in time |
| Fallback rate | `OrganicFallbackRate` (fell back / non-fault-injected cases) | Excludes cases that force a fallback by design |
| Mean (median) time per track | `Cases[].Elapsed`, over organic cases | Flex tier latency is heavy-tailed (single cases took 7 to 14 minutes in some runs), so the median is shown beside the mean; note the tier |
| Mean cost per track | `Cases[].CostUsd`, over organic cases | Includes organic cases that fell back or failed, since they spent tokens; estimate from configured prices |

**Organic cases** are the ones with `FaultInjected: false`, i.e. no `Force*` control in the golden set.
Forced cases fall back or retry by design and are cheaper and shorter on average (across all four configurations, 63 s and $0.020 per case against 96 s and $0.027 for organic cases), so including them would flatter
time and cost. Pass rate still covers every case, since it tests the whole set.

**Repeats.** A run is live and non-deterministic, so each configuration is run more than once to flatten out variation. Each configuration is run 3 times, and the mean and median values are taken for organic cases, with the pass rate covering both organic and fault-injected.

The generation table can be regenerated from the raw reports with `python3 -I docs/eval-results/summarise.py docs/eval-results/2026-10-09`. The critic numbers are read off the console summary in `critic/` and `critic-revised-fixtures/`: the last line, the `runs:` line under each fixture, and the `NOTE:` lines.

## Method notes

What was run:

- Tests used the real, in-process generation pipeline with live calls to OpenAI
- RAG used live calls to Voyage AI for embeddings, so it is possible for rate limiting to have an effect
- 4 model configurations (combinations of models for Generation, Repair and Critic)
- For each model configuration: 13 golden-set cases, 3 runs per model configuration. A total of 156 case-runs (4 configs x 13 cases x 3 runs)

What "Pass" means:

- Each case expects a specific outcome with an acceptable range of attempts
- Where fallback is expected, "Pass" indicates that the workflow fell back to the rule-based generation as expected
- Several cases are outcome-only, with a `maxAttempts` budget

Fault Injection:

- To effectively test validation failures or critic rejections, which are not deterministic, decorator classes were used to deliberately return failures/rejections as required
- Configurable exceptions are deliberately thrown from these wrappers to simulate e.g. OpenAI service outage

Rate-limit and quota handling:

- Cases that hit a 429 from Voyage AI never reached a model, so were rerun on their own
- An earlier attempt of `sol-luna6` and `luna6-all` hit an OpenAI insufficient credit balance error and were forced to fall back to rule-based generation. These runs were entirely discarded and fully re-run.

## Limitations

- **Cost is an estimate from configured prices, not an invoice.** Input and output tokens are priced
  separately, per model, and the service-tier multiplier (Flex) is applied on top.
- **Cached input is not discounted.** The step metrics record total input tokens only, so every input
  token is priced at the full input rate. If prompt caching applied, the real cost is somewhat lower.
- **Token counts were cross-checked against the OpenAI usage dashboard once.** On a single
  `baseline-pop-c` run (2026-10-09, `gpt-5.6-terra` generate, `gpt-5.6-luna` critic), the dashboard's
  total input and output tokens matched the harness's across both models. The dashboard has no
  per-model breakdown, so this confirms the totals, not each model's share. It was one case on one
  configuration; a model that bills reasoning tokens differently would need its own check.

- **The reggae case doesn't test drum density.** `baseline-reggae-i-iv-v` checks only that generation
  succeeds. Listening to the generated track, the drums were subjectively sparse (a hi-hat once per
  beat and little else), which no check in the harness would catch. See the rock timekeeping check
  (`baseline-rock-e-timekeeping` in [`GoldenSet/baseline.jsonl`](../api/eval/BackingTrack.Generation.Eval/GoldenSet/baseline.jsonl), with its expectation in [`CaseExpectations.cs`](../api/eval/BackingTrack.Generation.Eval/Model/CaseExpectations.cs))
  for the shape a fix would take.

- **The Flex timing is heavily tailed**. Timings using the less expensive Flex tier are unpredictable, and introduce noise into the measurements.

- **Small sample size**. 3 runs of 13 cases, 27 organic case-runs per config. Additional test fixtures will be added in the future.

- **Pass-rate saturation**. 3/4 configurations score a perfect 39/39 pass rate, so the pass rate alone separates nothing. Decisions need to be made based on critic attempts, time and cost.

- **Some statements have no committed evidence.** The raw output behind these isn't in `docs/eval-results/`, so they are the author's account and can't be checked from this page: the aborted Flex `gpt-6-luna` critic attempt, the discarded runs that hit the OpenAI credit limit, the usage-dashboard cross-check, and the listening note on the reggae drums.
