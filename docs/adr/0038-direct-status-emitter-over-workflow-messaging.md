# 0038. Direct status emitter over workflow-graph messaging for progress feedback

Status: Accepted

## Context

[ADR 0023](0023-client-derived-generation-progress.md) deferred backend-supplied stage messages
as "a separate, independent task" needing "the worker to write stage transitions into the job
store as the workflow runs". That task is this one: each of the generation pipeline's executors
now reports a stage message (and, for validate/critic, a pass/fail outcome) as it runs, and the
iOS progress screen polls `GenerationJobDto.FeedbackItems` to render a timeline.

The first implementation followed the pipeline's own pattern: every executor already
communicates by sending typed messages along graph edges (`IWorkflowContext.SendMessageAsync`),
so a new `StatusUpdateInput` message and a dedicated `StatusUpdateExecutor` node - wired as a
fire-and-forget edge off every stage - fit the existing shape and kept executors from taking a
direct dependency on `JobStore`.

In practice this made the feature nearly useless for the stages it mattered most for.
`Microsoft.Agents.AI.Workflows` runs in SuperSteps: `SendMessageAsync`'s own documentation says a
queued message "will be sent during the next SuperStep", and a SuperStep doesn't advance until
every executor active in it has fully returned. An executor that sends its status message as the
first line of its handler still doesn't get that message *delivered* to `StatusUpdateExecutor`
until its own handler - LLM call included - finishes. For Generation, Repair, Critic Review and
Fallback Generation, each backed by an LLM call running several seconds to tens of seconds, this
meant the "Generating a candidate track..." message reliably arrived only once generation was
already done, with a backdated timestamp making it look live in hindsight. The screen this ADR
exists to serve - watching what the pipeline is doing right now - didn't work for the slow
stages, which are exactly the ones a user is watching.

## Decision

Executors report status through `IJobStatusEmitter`, a narrow interface
(`Emit(jobId, stage, attempt, message)`) implemented by `JobStatusEmitter`, which writes straight
into `JobStore.AppendJobFeedback`. This bypasses the workflow graph's messaging entirely - no
`StatusUpdateInput`, no `StatusUpdateExecutor`, no status-update edges. The write is synchronous
and effectively instant (`JobStore` is an in-memory `ConcurrentDictionary`), so a client poll
immediately after the call sees the update.

## Alternatives considered

**Split each slow executor into an "announce" node and a "do the work" node.** Since a SuperStep
runs its active executors concurrently, putting the status-send in its own upstream node would
land it one SuperStep ahead of the slow work, letting `StatusUpdateExecutor` and (say)
`GenerateCandidateTrackExecutor` run in the same later SuperStep - the status would land while the
LLM call is still in flight. Rejected: it keeps the framework's SuperStep timing as a load-bearing
detail future contributors have to understand, and needs a new executor class (plus rewiring) for
every slow stage, for a problem the direct-write approach sidesteps outright.

**Fire the status send with `Task.Run`, still through the workflow message.** Doesn't help -
the delay isn't in when `SendMessageAsync` is *called*, it's in when the engine *delivers* an
already-queued message, which a caller-side `Task.Run` has no influence over.

**Keep the workflow-message pattern for consistency, accept the delayed delivery.** Rejected as a
non-fix: the whole point of the feature is a client watching a stage in progress, not a replay of
what already happened by the time it's shown.

## Consequences

Executors now depend on `IJobStatusEmitter` rather than only `IWorkflowContext` for this concern -
a second channel alongside the graph's own messaging, not a strict layering violation since the
emitter is a narrow, mockable seam (unit tests substitute it directly, same as any other
collaborator), but it does mean "how does this executor talk to the rest of the system" no longer
has one answer. Any future engineer wiring a new executor needs to know status updates are the
one thing that bypasses the graph, and why.

This also means status messages are no longer part of the workflow's own event stream or replay
model (checkpointing, if this workflow ever adopts it, would not include them) - acceptable here
since `FeedbackItems` is presentation-only, not decision-making state the pipeline itself depends
on.
