# 0034. Rule-based fallback generator bypasses the critic loop

Status: Accepted

## Context

The scope doc's Precise-mode flow promises a "never fail" guarantee: if the LLM generation ->
validate -> repair loop can't produce a track that passes deterministic validation within
`GenerationMaxAttempts`, a deterministic rule-based generator should produce one instead, so the
app always returns *something* playable rather than failing the job outright.

Before this change, both terminal-failure branches in the workflow —
`ValidateCandidateTrackExecutor` exhausting `GenerationMaxAttempts`, and `CriticExecutor`
exhausting its own critic-review attempt budget — simply yielded `GenerationFailed`. Building the
fallback generator meant deciding exactly where in the workflow graph it plugs in, and in
particular whether its output should be treated like any other candidate track (i.e. still subject
to the critic's musical-quality review) or handled as its own terminal path.

## Decision

The fallback generator is its own executor (`FallbackGenerateCandidateTrackExecutor`), reached from
*both* exhaustion points (`ValidateCandidateTrackExecutor`'s max-attempts branch and
`CriticExecutor`'s max-attempts branch) via `FallbackGenerateCandidateTrackInput`. Its output is
re-validated once by `ValidateCandidateTrackExecutor` (`IsFallbackTrack: true`) purely as a
correctness assertion — the deterministic construction (bass roots, block chord voicings, a fixed
drum pattern, all drawn from the same already-matched chord/scale data the validator checks
against) should always pass by construction — and on success goes straight to
`PersistCandidateTrackExecutor`, never to `CriticExecutor`. If that re-validation somehow fails,
the job fails loudly (`GenerationFailed`, logged as an error) rather than falling through into the
LLM repair loop.

The persisted job additionally carries a `WasFallback` flag (`GenerationSucceeded` ->
`GenerationJob.State == JobStates.CompletedViaFallback`), surfaced to the iOS app, which already
had UI built for this state ahead of the generator existing.

## Alternatives considered

- **Route the fallback track through the critic like any other candidate.** Rejected on two
  grounds: (1) the critic is a musical-taste check on generative output — the fallback pattern is
  deliberately as plain as possible ("start generic, iterate" per the design discussion), so a
  critic verdict on it is not actionable information; (2) more importantly, `critic -> generate` is
  an existing edge in `GenerationWorkflowBuilder` — a critic rejection sends the track back into
  LLM generation. Routing the fallback through the critic would risk looping straight back into the
  same agentic pipeline that just exhausted its budget, defeating the "never fail" guarantee the
  fallback exists to provide.

- **On fallback re-validation failure, fall through into the existing repair branch** (since the
  fallback's `ValidateCandidateTrackInput` reuses the same executor and attempt-counting code as a
  normal candidate). Rejected: `RepairCandidateTrackInput`/the `ValidateCandidateTrackInput` it
  sends don't propagate `IsFallbackTrack`, so a "repaired" fallback track would silently re-enter
  the normal flow (including becoming critic-eligible) with no record that it originated from a
  fallback bug. A should-never-happen path in a "never fail" system needs to fail loudly at the
  source, not reroute through the exact loop it's independent of.

## Consequences

- The fallback generator only needs to satisfy `TrackValidator`'s deterministic checks, never the
  critic's subjective quality bar — simplifies its design and keeps it free of any dependency on
  the critic step or its settings.
- Two separate places in the workflow (`ValidateCandidateTrackExecutor`,`CriticExecutor`) now both
  know about `FallbackGenerateCandidateTrackInput` and must stay consistent about when they trigger
  it. A future third terminal-failure path (if one is ever added) needs to remember this pattern
  rather than defaulting to `GenerationFailed`.
- `WasFallback` must be threaded through every record between `ValidateCandidateTrackExecutor`'s
  fallback-success branch and `GenerationJobWorkerHostedService` (`PersistCandidateTrackInput`,
  `GenerationSucceeded`) — anyone adding a new field to the success path needs to remember to keep
  this flag flowing too, since nothing enforces it structurally.

## Update — 2026-10-07

The "never fail" guarantee above only covered *budget exhaustion*. The generic `catch (Exception)`
blocks in `GenerateCandidateTrackExecutor`, `RepairCandidateTrackExecutor` and `CriticExecutor`
still yielded `GenerationFailed`, so an LLM outage, timeout or malformed response failed the job
and the fallback never ran — the "future third terminal-failure path" the Consequences section
warned about. All three now send `FallbackGenerateCandidateTrackInput` instead. The
`OperationCanceledException` branches are unchanged: a cancelled job is still cancelled, not
rescued. An HttpClient timeout throws `TaskCanceledException` with the workflow's token still
live, so the `when (cancellationToken.IsCancellationRequested)` filter is false and it takes the
fallback path. Reaching the fallback from generate and repair needed two new workflow edges
(`generate → fallback`, `repair → fallback`) alongside the existing `critic → fallback`.

Decisions made along the way:

- **A critic outage falls back to the rule-based track too, rather than persisting the validated
  candidate unreviewed.** Persisting would keep a better track (it already passed deterministic
  validation), but it needs a third success shape — validated, unreviewed, no `FallbackReason` — in
  `PersistCandidateTrackInput`, `GenerationSucceeded` and the iOS app. Falling back matches what
  critic *exhaustion* already does, so every critic-side terminal path ends the same way. Revisit
  if critic outages turn out to be common enough that discarding good candidates hurts.
- **The catch stays broad.** It also swallows programming errors and permanent faults (a revoked
  API key) that no fallback fixes, so those jobs now "succeed" quietly via the fallback. The
  executors log the exception at `Error` with the fallback noted, and the metrics make it
  visible: `generation_job.completed{outcome="completed_via_fallback"}` gives the fallback rate,
  and the per-call `track_generation.calls`, `track_repair.calls` and `critic.calls` counters
  (tagged `outcome="failure"`) show when the LLM calls themselves are failing.
- **Every fallback records why.** Without a reason, telling an outage from a hard input meant
  correlating those two series by time. `FallbackGenerateCandidateTrackInput` now carries a
  required `FallbackReason` (`ValidationExhausted`, `CriticExhausted`, `GenerateError`,
  `RepairError`, `CriticError`), threaded through the validate and persist inputs to
  `GenerationSucceeded` — replacing the old `WasFallback` bool, which is now just
  `FallbackReason is not null` — and the worker tags `generation_job.completed` with
  `fallback_reason` for fallback jobs only. It is passed to `RecordOutcome` as a parameter rather
  than stored on `GenerationJob`: the metric is its only consumer, and the job record is
  deliberately kept free of anything not needed by clients or the status path. Revisit if the
  reason ever needs to reach the status endpoint or the app. Because the parameter is required, a
  sixth trigger can't compile without choosing a reason. The tag values are explicit strings
  (`FallbackReasonExtensions.ToMetricTag`), pinned by tests like the other dashboard-contract tags.
- **Retrieval (Voyage) failures stay terminal.** The fallback builds from the matched chords and
  scales, which retrieval produces, so there is nothing to fall back *with*.
- **No new loop risk.** A fallback track that fails its own validation still fails loudly, so the
  new edges can't route back into the LLM loop.

Consequences:

- Four executors now send `FallbackGenerateCandidateTrackInput`, from five triggers:
  `ValidateCandidateTrackExecutor`'s max-attempts branch, `CriticExecutor`'s max-attempts branch,
  and the generic catch in each of generate, repair and critic. They must stay consistent about
  when they fire, and each needs its own `SendsMessage` declaration and workflow edge. The required
  `FallbackReason` makes forgetting to say *why* a compile error; it can't make a new trigger
  remember the edge.
- Executor unit tests only check the message an executor sends, so a missing edge passes them all.
  Two things check the routing. `GenerationJobWorkerOutcomeMetricsTests` runs the real workflow
  graph with substituted steps and covers all five triggers end to end, asserting the job completes
  via fallback with the right `fallback_reason`. The `fallback-llm-outage` eval case
  (`forceGenerateExceptions` + `FaultInjectingGenerateStep`) does the same for `generate → fallback`
  against the real retrieval step; with the edge removed it fails with "workflow produced no
  GenerationOutcome". The eval has no repair or critic equivalent, which the worker tests cover.
