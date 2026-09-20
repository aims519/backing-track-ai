# 0036. Per-step model and Flex-tier split for LLM cost, over a single model everywhere

Status: Accepted

## Context

The generation pipeline made every LLM call — generate, repair, critic — with the same model
(`gpt-5.6-terra`) at OpenAI's standard processing tier. OpenAI shipped two cost levers worth
evaluating: a cheaper model tier in the `gpt-5.6` family (`gpt-5.6-luna`, ~10x cheaper per token
than `gpt-5.6-terra`), and Flex processing (roughly half the standard token price in exchange for
variable, sometimes much higher, latency — OpenAI's own guidance recommends it for background
jobs and evals specifically).

A single blanket swap to `gpt-5.6-luna` risked a quality regression nothing here was measuring for.
`api/eval/BackingTrack.Generation.Eval` (pipeline outcomes) and `api/eval/BackingTrack.Critic.Eval`
(critic accuracy/consistency against known-good and known-flawed fixtures) already existed as the
regression net for exactly this kind of change (see [ADR 0027](0027-generation-eval-harness.md)),
so the question was answered empirically rather than by guessing.

## Decision

Run the golden set and critic fixtures across several model/tier combinations and compare:

- **All-`gpt-5.6-luna`** (generate + repair + critic): 9/9 pipeline cases passed, but
  `BackingTrack.Critic.Eval` caught a real weakness — the critic itself was inconsistent on
  `VoiceLeading` judgments, false-positiving a known-good fixture (2 of 5 repeats) that should
  have passed clean.
- **`gpt-5.6-luna` generate/repair, `gpt-5.6-terra` critic**: isolates whether the voice-leading
  problem is generation quality or critic flakiness. With a critic already trusted to judge
  correctly, 5 separate generations still drew genuine `VoiceLeading` citations, each describing
  the same defect — chord voicings moving as a parallel block instead of retaining common tones or
  moving individual voices by step. This confirmed the weakness is in Luna's *generation*, not in
  whichever model does the judging, and it concentrated hardest on the toughest prompt in the set
  (`validator-odd-meter-funk`, an odd-time-signature case — directly relevant to this project's
  odd-meter/style generation work).
- **`gpt-5.6-terra` generate, `gpt-5.6-luna` repair + critic (Flex)**: genuine `VoiceLeading`
  citations dropped from 5 to 1 across the same 9 cases, confirming generation quality is what
  drives the defect. Repair degrades safely on a weaker model regardless: `RepairStep.IsEmpty`
  already falls back to returning the previous attempt unchanged if the patch is empty/unusable,
  so a wrong repair call doesn't corrupt output, only wastes a call.
- **Adding Flex to the generation step too**: no regression — 9/9 pass, quality unchanged, and the
  run actually completed faster than the non-Flex equivalent (Flex latency is variable, not
  uniformly slower). Track generation runs as a background job the iOS app polls via job state
  (see the state-machine design), not a request a user blocks on, so Flex's variable/higher
  latency is an acceptable tradeoff there.

Landed configuration: `GenerationModel = gpt-5.6-terra`, `RepairModel = CriticModel = gpt-5.6-luna`,
all three steps on the Flex service tier (`OpenAiSettings.{Generation,Repair,Critic}ServiceTier`).

Flex's variable/queued processing routinely exceeded the OpenAI .NET SDK's default request
timeout (100s) — reproduced live as a real eval failure (`validator-silent-bar-long-vamp` timing
out after 4 retries). Fixed by bumping `OpenAIClientOptions.NetworkTimeout` to 15 minutes
specifically for Flex-tier clients, per OpenAI's own guidance for Flex workloads.

## Alternatives considered

- **All-`gpt-5.6-luna`**: cheapest, but the critic eval showed genuine unreliability on
  `VoiceLeading` verdicts, and the generation-only test (below) showed the underlying defect is
  real, not just a flaky critic. Rejected: fails on the exact dimension (voice leading, on hard
  prompts) this project's generation work is trying to improve.
- **Keep everything on `gpt-5.6-terra`**: no risk, no savings. Rejected as leaving cost reduction
  on the table when repair and critic held up fine on the cheaper model.
- **Flex only on repair/critic, standard tier on generation**: the original plan, kept cautious
  because generation was assumed to be more latency-sensitive. Testing showed no such
  sensitivity — generation already runs as an async background job — so this would have left
  savings on the table for no real benefit.

## Consequences

- Quality now depends on which step a given model is doing, not just which model — a future model
  swap on any one step needs its own eval run rather than being assumed safe by analogy with
  another step.
- Flex's variable latency means individual generation jobs can occasionally take much longer than
  before, up to the new 15-minute ceiling; this is invisible to the API's job-polling clients but
  worth remembering if the workflow's own timeouts/attempt budgets are ever tightened.
- The three eval-harness/production model-tier decisions above are the *result* of this specific
  eval run, not a load-bearing guarantee — if the golden set or fixtures change meaningfully, or
  OpenAI's Flex tier changes on GA, this split should be re-verified rather than assumed to still
  hold.
