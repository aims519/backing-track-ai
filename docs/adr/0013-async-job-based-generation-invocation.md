# 0013. Async job-based invocation model for the generation pipeline

Status: Accepted

## Context

Both generation modes described in the scope doc have latency that doesn't fit a plain
synchronous request/response cleanly. Precise mode's RAG retrieve + LLM generate +
validate/repair loop is estimated at roughly 3-10s typical, up to ~20-25s worst case with
bounded repair retries before falling back to the rule-based generator. Vibe mode's third-party
audio-generation API (ElevenLabs Music/Stable Audio-style) renders actual audio rather than
structured text/JSON, a different order of magnitude of work, estimated at roughly 10-60s+.

A blocking HTTP call in the tens-of-seconds range is a poor fit for the iOS client
(`URLSession` timeouts, cellular flakiness, the app backgrounding mid-request). Vibe mode alone
already ruled out a synchronous call; once that was true, keeping one consistent invocation
model across both modes was preferred over sync-for-Precise/async-for-Vibe, to keep the client
simple.

## Decision

A request creates a job with an id and returns immediately; the client polls a status endpoint
using that id.

Job states: `Pending` -> `InProgress` -> `Complete` | `CompletedViaFallback` | `Failed`.
`CompletedViaFallback` is a distinct terminal state from `Complete` so the rule-based-fallback
trigger rate can be reported separately from genuine LLM-path success, matching the scope doc's
intent to produce real numbers (e.g. fallback trigger rate) for the portfolio writeup rather than
folding that signal into logs alone.

No completion percentage is reported. The client stops polling as soon as it sees any terminal
state (`Complete`, `CompletedViaFallback`, `Failed`).

The job store is in-memory (no persistence across restarts). If a job vanishes because the
process restarted mid-job, the client sees the same generic failure as any other `Failed` job
("there was a problem with your request, please try again") rather than a distinct error case.

## Alternatives considered

Plain synchronous request/response — rejected. Tolerable for Precise mode's latency alone, but
not for Vibe mode's, and a mixed sync/async model per endpoint was rejected in favor of one
consistent client-side pattern.

A full message-broker/job-queue system (e.g. Hangfire, a dedicated queue) — rejected as
unnecessary infrastructure for a single-user, low-concurrency Pi-hosted app. Same reasoning as
the k3s rejection in the project scope doc: the complexity buys orchestration capability this
project doesn't need.

Progress percentage reporting — rejected. Precise mode's discrete steps could support a rough
percentage, but Vibe mode's third-party API exposes no real progress signal, so a percentage
there would be cosmetic/fake. Reporting it for only one mode was rejected as inconsistent.

Persisting jobs (e.g. in SQLite) across restarts — rejected for now, to keep the job store to a
simple in-memory dictionary. A mid-job restart losing the job, with the client retrying, was
accepted as a reasonable failure mode at this stage.

## Consequences

The client must implement polling (interval, and stopping on any terminal state) rather than
getting a single result back from the initial request, which is more client-side complexity than
a synchronous call would need.

A process restart mid-job silently loses that job with no distinct signal to the client beyond a
generic failure — acceptable for a single-user app today, but if usage or reliability
expectations grow, this is a real gap: the fix (persisting job state) is a rework of the job
store, not a config change.

Without a percentage or other progress signal, the client has no way to distinguish "job is
progressing normally" from "job is stuck," beyond the states already defined. If jobs ever need a
stuck/timeout state, that's an additional state to design, not something this model already
covers.
