# 0037. Streaming run API for cancellable generation jobs

Status: Accepted

## Context

Adding a `/cancel/{jobId}` endpoint requires the in-flight generation workflow to actually stop — not just have the job's recorded state flip to `Cancelled` while the underlying work keeps running.

The generation workflow runs in-process via the Microsoft Agent Framework's `Microsoft.Agents.AI.Workflows` package (`InProcessExecution`), driven by `GenerationJobWorkerHostedService`. The first implementation passed a per-job `CancellationTokenSource` into `InProcessExecution.RunAsync(..., cancellationToken:)`, expecting cancellation to propagate down into each workflow executor's `HandleAsync(..., cancellationToken)` and trigger an `OperationCanceledException` at whatever async call was in flight (e.g. the LLM call in `GenerateCandidateTrackExecutor`).

In testing, cancelling that token did not stop the in-flight executor. Decompiling the framework (`Microsoft.Agents.AI.Workflows` 1.20.0) showed why: `RunAsync`'s `cancellationToken` only gates the caller's own wait for the next batch of events (`Run.RunToNextHaltAsync` → `AsyncRunHandle.TakeEventStreamAsync`, whose consumer is a `NonThrowingChannelReaderAsyncEnumerable` — cancellation ends the enumeration without throwing). The actual workflow execution runs on a separate background loop (`StreamingRunEventStream.RunLoopAsync`) started via `Task.Run`, driven by its own private `CancellationTokenSource` (`_runLoopCancellation`), entirely decoupled from the token passed into `RunAsync`. Cancelling our token only stopped our own foreground read of already-produced events; the background loop, and whatever executor it was running, kept going to completion regardless.

## Decision

Switch the worker from `InProcessExecution.RunAsync` to `InProcessExecution.RunStreamingAsync`, which returns a `StreamingRun` immediately (rather than only after the run halts). `StreamingRun.CancelRunAsync()` cancels the run loop's own internal token — the same token that reaches each executor's `HandleAsync` — so it's the one API in this package that actually stops in-flight work.

`JobStore` registers the `StreamingRun` per job (replacing the earlier `CancellationTokenSource` registration). `/cancel` calls `JobStore.CancelAsync`, which marks the job `Cancelled` in the store *before* calling `run.CancelRunAsync()`, then removes and disposes the run once the worker's processing loop finishes.

Marking the job `Cancelled` up front (rather than waiting for the workflow's own output) is necessary because `CancelRunAsync()`'s effect on the consumer side is also racy: cancelling ends our event-stream enumeration immediately, which can happen before the executor's own cooperative `catch (OperationCanceledException)` block has run and yielded a `GenerationCancelled` output. The worker's outcome-handling switch re-checks the job's current state and will not let a late-arriving `Succeeded`/`Failed`/`Cancelled` outcome overwrite a cancellation that was already recorded.

## Alternatives considered

- **Keep the per-job `CancellationTokenSource` passed into `RunAsync`.** Rejected: confirmed by decompiling the framework that this token is never wired into the run loop or its executors — cancelling it is a no-op for anything already in flight. Verified in testing: cancelling mid-`GenerateCandidateTrackExecutor` let the LLM call, and the workflow steps after it (`CriticExecutor`), keep running and logging well after the job had already been marked `Cancelled`.
- **Infer cancellation purely from the workflow's output (a `GenerationCancelled` event).** Rejected: even with `StreamingRun.CancelRunAsync()` genuinely cancelling the executor, the consumer-side event stream can end before that executor's cancellation handling finishes and yields its output — the executor's own cancellation log line was observed arriving tens of milliseconds *after* the worker had already concluded (from a null outcome) that the job was cancelled. Treating the `/cancel` call itself as the source of truth for state avoids depending on that race resolving a particular way.

## Consequences

- Cancelling a job now genuinely interrupts in-flight LLM calls and workflow steps, rather than merely relabeling a job that continues running to completion in the background.
- The `StreamingRun` must be unregistered from `JobStore` before it is disposed (not after, via `await using`) — disposing first and unregistering second reopens the same `ObjectDisposedException` race a raw `CancellationTokenSource` had, since `CancelRunAsync()` can be called concurrently by `/cancel` on an already-disposed run.
- The workflow-level "cancelled between steps" ambiguity is real and permanent, not a bug to be fully eliminated: log messages and job state transitions have to be read as "cancellation was requested at time T," not "the workflow stopped at time T" — those are two different moments.
- Host shutdown (`stoppingToken`) still does not cancel in-flight generation via this path; only an explicit `run.CancelRunAsync()` call does. Graceful shutdown of in-flight jobs, if wanted later, needs its own explicit wiring (e.g. `stoppingToken.Register(...)`) rather than relying on the token already threaded through `RunStreamingAsync`.
