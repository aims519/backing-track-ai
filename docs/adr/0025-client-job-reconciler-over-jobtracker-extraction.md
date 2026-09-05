# 0025. A standalone client-side JobReconciler, not the JobTracker extraction

Status: Accepted

## Context

The job-history screen ([ADR 0024](0024-client-side-swiftdata-job-history.md)) has to
reconcile on open: most rows sit at `.inFlight` with a stale status because dismissing
`GenerationProgressView` before a terminal state is the normal action, so the screen
must re-poll those jobs and write the result through.

ADR 0024 anticipated this and named it the moment to do a refactor:

> **Anticipated refactor:** extract a `JobTracker` that owns polling + timeout +
> classification + persistence and exposes the job's state as an `AsyncStream`;
> `GenerationProgressViewModel` then only maps that to `Phase`. The history-screen task
> is the right time — its reconcile-on-open pass is the second caller of "poll a job and
> persist the result".

ADR 0024 also framed `JobHistoryStore` as a pure mirror of the poll loop — every write
originates from `GenerationProgressViewModel` or `GenerateTrackViewModel` following a
server response.

Two things pushed against doing the extraction now. The reconcile pass is a *one-shot*
check, not a loop: no timeout budget, no repeated polling, and it wants gentler error
handling (a network or auth failure should say nothing about the job and leave the row
untouched, where the live loop eventually gives up and writes `.pollError`). And the
history screen also needs a genuinely new kind of write — a user deleting a row — which
isn't a mirror of anything.

## Decision

**Add a small standalone `JobReconciler` and leave the live poll loop alone. Give
`JobHistoryStore` a `delete`.**

- `JobReconciler` (`app/BackingTrack/Persistence/JobReconciler.swift`) is a `@MainActor`
  struct with one method, `reconcile(entry:) async -> Outcome`. It polls the row's job
  once (twice on a first 404, to match the live loop's one-blip tolerance) and folds any
  result back through the existing `JobHistoryStore` methods (`recordStatus`,
  `recordDisposition`). `Outcome` is `unchanged` / `advanced(JobDisposition)` / `lost` /
  `unreachable(APIClientError)` / `notInHistory`.
- Its error mapping is deliberately simpler and gentler than
  `GenerationProgressViewModel.classify`: any transport / 5xx / rate-limit / auth error
  returns `.unreachable` and writes **no** disposition — the row stays as it was and the
  screen shows a transient "couldn't refresh". Only a sustained 404 is terminal, written
  as `.lost`.
- `GenerationProgressViewModel` and its poll loop are unchanged. `HistoryViewModel` owns a
  `JobReconciler` and calls it from the screen's `.task` and `.refreshable`.
- `JobHistoryStore` gains `delete(jobID:)` — the first user-initiated write, as opposed to
  the poll-loop mirror the other methods serve. The protocol doc says so.

## Alternatives considered

**Extract `JobTracker` now** (ADR 0024's anticipated refactor). Rejected for this task.
The one-shot reconcile and the live loop share the shape "poll, classify, persist" but
diverge on everything underneath — retry budget, deadline, and what a failed poll means.
Folding both behind one `AsyncStream`-emitting type means that type carries two modes, and
its interface gets designed against a *guess* about the eventual third consumer (the
player screen's download-and-cache pass) rather than against real ones. Deferring keeps
this change to "add a screen", and lets the `JobTracker` boundary be drawn later when
playback is a concrete second caller instead of a forecast. The extraction is still the
right eventual consolidation — this defers it, it doesn't cancel it.

**Reuse `GenerationProgressViewModel.classify` for the reconcile.** Rejected. It's built
for a loop with a failure budget: it counts consecutive failures, and it maps
`.unauthorized` to `.giveUp(.pollError)` — a permanent disposition. A manual refresh over
a temporarily-bad API key must not brand a historical row `.pollError` forever, and a
one-shot has no "next poll" for a budget to matter. The mappings genuinely differ.

**Delete straight from the view via `@Environment(\.modelContext)`.** Rejected. ADR 0024
established `JobHistoryStore` as the single write path. Keeping delete there means
`HistoryViewModel` stays unit-testable against the existing `RecordingJobHistoryStore`
fake, and the call keys on a `UUID` like every other store method — no non-`Sendable`
`@Model` handed across to a view model.

## Consequences

- "Poll a job, decide what it means, persist it" now has two implementations —
  `GenerationProgressViewModel`'s loop and `JobReconciler` — with similar but
  intentionally divergent classification. A change to how one treats a poll result (a
  404, say) has to be weighed for both. This is the duplication the `JobTracker`
  extraction would remove; it is still pending and still the right place to remove it.
- `JobReconciler`'s auth-error handling differs from the live loop on purpose. It's called
  out in the doc-comment, but a reader comparing the two files will see the divergence and
  has to know it's deliberate.
- `JobHistoryStore` is no longer purely a poll-loop mirror. `delete` is user intent; a
  future retention / `prune` hook (ADR 0024, Consequences) would join it as a second
  non-mirror write.
- A reconcile that 404s settles the row at `.lost` permanently, even for a job that may
  have finished server-side — no `trackId` was ever captured, and the in-memory job store
  won't have it back after the restart that dropped it ([ADR 0013](0013-async-job-based-generation-invocation.md)).
  The history UI renders `.lost` as "may have finished — can't confirm" rather than a
  failure, which is the honest framing, but there is no recovery path.
