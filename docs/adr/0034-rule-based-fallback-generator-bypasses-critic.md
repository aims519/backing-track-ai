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
