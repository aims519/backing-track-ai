# 0049. `gpt-6.1-sol` for the generate step, over `gpt-5.6-terra` or an all-Luna pipeline

Status: Accepted

Partially supersedes [0036](0036-per-step-model-tier-split-for-cost.md): only its generation model.
Luna for repair and critic, and Flex on all three steps, still stand.

## Context

[ADR 0036](0036-per-step-model-tier-split-for-cost.md) landed on `gpt-5.6-terra` for generation
because the cheaper `gpt-5.6-luna` produced real `VoiceLeading` defects. Newer models have shipped
since (`gpt-6.1-sol`, `gpt-6-luna`), and the eval harness now records model, token usage, fallback
and estimated cost per run, so the comparison could be repeated on measured numbers.

## Decision

Use `gpt-6.1-sol` for generation, with `gpt-5.6-luna` for repair and critic, all on Flex. The
results behind this are in [`docs/eval-results.md`](../eval-results.md): four configurations,
3 runs each over the 13-case golden set.

Against the previous `gpt-5.6-terra` configuration, `gpt-6.1-sol` had:

- no fallbacks in 27 organic case-runs, against 1;
- fewer attempts (1.00 against 1.19) and critic attempts (1.19 against 1.46);
- a lower estimated cost per track ($0.031 against $0.044);
- a longer mean time per track, 80.7 s against 40.5 s.

The extra latency is accepted because generation runs as a background job and the app is notified
when it finishes ([ADR 0039](0039-apns-push-from-api-on-terminal-job-state.md)).

## Alternatives considered

- **Keep `gpt-5.6-terra`**: roughly twice as fast, but one fallback in 27, more rejected drafts, and
  higher cost per track. Rejected on reliability and cost.
- **`gpt-6-luna` for every step**: about a tenth of the cost ($0.003 per track), and it passed every
  case, but its first drafts were rejected more often (1.59 critic attempts against 1.15) and tracks
  took about 60% longer (162 s against 100 s). Rejected because the cheap generator spends the saving
  on retries and wait time, which is the weakness ADR 0036 found in `gpt-5.6-luna`.
- **`gpt-6-luna` for repair and critic**: indistinguishable from `gpt-5.6-luna` on pass rate, cost
  and critic attempts, and slightly slower on the mean. Kept `gpt-5.6-luna`; the Flex timings are too
  noisy to read much into the gap.

## Consequences

- Pass rate does not separate the configurations (three of four scored 39/39), so the choice rests
  on critic attempts, fallback, time and cost over 27 organic case-runs per configuration. That is a
  small sample, and Flex latency is heavy-tailed.
- The eval used a generation budget of 3 attempts. The API uses 5 in Production and 10 in
  Development, so production has more repair headroom than was tested.
- Mean generation time roughly doubles against `gpt-5.6-terra`. Re-check this if generation is ever
  put in front of a user who is waiting.
- As with ADR 0036, this is the result of one eval run. A model swap on any step, or a meaningful
  change to the golden set or critic fixtures, needs a rerun rather than being assumed safe.
