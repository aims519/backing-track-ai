# 0043. TTL eviction of finished jobs from the in-memory job store

Status: Accepted

## Context

[ADR 0013](0013-async-job-based-generation-invocation.md) kept jobs in an in-memory
`JobStore`, and nothing ever removed one: `JobStore.Remove` existed but had no callers, so
every job (its chords, feedback items and state) stayed in memory until the process
restarted. [ADR 0023](0023-client-derived-generation-progress.md) listed this as a known gap.
On a Raspberry Pi that is a slow leak rather than a crash, but it is unbounded.

Eviction has a client-visible cost. A missing job is a plain `404` (ADR 0013), and the iOS
history reconcile treats a `404` as permanent: the row settles at `.lost` and no `trackId` is
ever recovered ([ADR 0025](0025-client-job-reconciler-over-jobtracker-extraction.md)). So
whatever is evicted can no longer be resolved by a client that hadn't already fetched it.

## Decision

Finished jobs are evicted after a time-to-live, by a background sweep.

- **Only terminal jobs** (`Complete`, `CompletedViaFallback`, `Failed`, `Cancelled`) are
  eligible, judged by `EndTime`. `Pending` and `InProgress` jobs are never evicted.
- **A hosted service** (`GenerationJobEvictionHostedService`) runs a `PeriodicTimer` and calls
  `JobStore.EvictTerminatedJobs(cutoff)`. Defaults are a 3600s TTL and a 60s sweep interval,
  bound from the `JobEviction` config section and validated at startup (source-generated
  `[OptionsValidator]`, as for the other settings classes) so a missing or non-positive value
  fails the host with a clear error instead of crashing the timer.
- **Removal is by instance** (`TryRemove(KeyValuePair)`), so a job updated between being
  inspected and being removed is left alone.
- **`TimeProvider`** drives both the cutoff and the timer, so tests advance a fake clock
  instead of waiting out the TTL.

## Alternatives considered

**Evict as soon as a job reaches a terminal state.** Rejected. The client learns the outcome
from a push ([ADR 0039](0039-apns-push-from-api-on-terminal-job-state.md)) or its next poll,
and then reads the status to capture `trackId`. Immediate removal makes that read race the
eviction and turns every slow client into a `.lost` row.

**Evict lazily on `TryGet` / `AddOrUpdate`.** Rejected. Eviction would depend on traffic, a
quiet server would never reclaim memory, and a read path would gain a side effect that a
status poll shouldn't have.

**Keep everything (status quo).** Rejected as the memory use is unbounded, though it is
what ADRs 0013 and 0024 accepted for a small personal deployment.

**Persist jobs (e.g. Postgres) and drop the TTL concern.** Not rejected, deferred: it is a
separate, larger piece of work. If it lands, in-memory remains the dev/test default, so this
sweep stays useful there.

## Consequences

- A client that was offline for longer than the TTL, and had not captured the `trackId`,
  sees `.lost` ("may have finished — can't confirm") even if the job succeeded. The track file
  is still on disk (ADR 0014 has no cleanup), but nothing on the client can find it. Raising
  the TTL narrows this window at the price of memory; it does not remove it.
- Wedged `InProgress` jobs are still never evicted, so the "stuck forever" half of the ADR
  0023 gap remains. Evicting them would need a timeout state, which is a separate decision.
- An evicted job's `/cancel` returns `404`, the same as an unknown id.
- A job can outlive its TTL by up to one sweep interval.
- `EndTime` is stamped from `DateTimeOffset.UtcNow`, not `TimeProvider`, so only the sweep is
  clock-injectable; tests that exercise it end to end start the fake clock at real "now".
