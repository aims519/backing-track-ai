# 0031. Message-threaded attempt counters over a scoped GenerationJobContext

Status: Accepted

## Context

Wiring the critic review into the generation workflow (`validate -> critic -> generate`, looping
back on a failed review) needed its own bounded-retry counter, separate from the existing
`Attempts` used by the `validate -> repair -> validate` loop: the two loops fail for different
reasons (deterministic rule violations vs. subjective LLM judgment) and can legitimately need
different limits, so sharing one counter would make one loop's budget arbitrarily depend on how
many attempts the other loop already spent.

Each workflow executor (`GenerateCandidateTrackExecutor`, `ValidateCandidateTrackExecutor`,
`RepairCandidateTrackExecutor`, `CriticExecutor`) runs from a DI scope created fresh per job
(`GenerationJobWorkerHostedService`), which made a scoped, mutable context object a live option
for carrying `ValidationAttempts` and the new `CriticAttempts` across executors without touching
every input record's constructor.

## Decision

Add `CriticAttempts` as an explicit field threaded through every input record on the path it can
appear on (`GenerateCandidateTrackInput`, `ValidateCandidateTrackInput`, `RepairCandidateTrackInput`,
`CriticInput`), alongside the renamed `ValidationAttempts`. Each executor passes the counter
through unchanged unless it owns the loop that increments it: `RepairCandidateTrackExecutor`
increments `ValidationAttempts`, `CriticExecutor` increments `CriticAttempts`. Attempt-cap checks
live in the executor that is about to loop back (`ValidateCandidateTrackExecutor` for
`ValidationAttempts` against `GenerationMaxAttempts`, `CriticExecutor` for `CriticAttempts`
against `CriticSettings.MaxAttempts`), checked immediately after incrementing rather than on the
next message's arrival, so an exhausted budget fails before spending another generate+validate
round.

## Alternatives considered

- **A scoped `GenerationJobContext` injected into each executor, holding `ValidationAttempts`,
  `CriticAttempts`, and similar per-job counters.** Rejected. It would work safely — the per-job
  DI scope means it can't leak across concurrent jobs — but it moves state that currently lives
  in each executor's typed input record into ambient state an executor's method signature no
  longer reveals. This workflow is a message-passing graph specifically so that each hop's
  behavior is traceable from its input type alone; the `PreviousAttempt`-not-threaded-through bug
  found while wiring this loop was exactly the kind of silent, hard-to-see gap that hidden
  ambient state would make more likely, not less.

## Consequences

- Every new counter added to a loop has to be threaded through every input record on its path,
  even records that only pass it along unchanged (e.g. `GenerateCandidateTrackExecutor` never
  reads `CriticAttempts`, only forwards it to `ValidateCandidateTrackInput`). More constructor
  plumbing than a shared context would need.
- Each executor's input record is still a complete, self-contained description of what that hop
  knows, which keeps the workflow easy to unit-test per executor (`FakeWorkflowContext` plus a
  plain input value) and easy to reason about if executors are ever replayed or run concurrently.
- If a future counter needs to be visible to many unrelated executors (not just threaded along a
  linear path), revisit the scoped-context option rather than continuing to grow every input
  record.
