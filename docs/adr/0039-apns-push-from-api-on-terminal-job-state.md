# 0039. APNs push from the API on terminal job state, over client-side background completion

Status: Accepted

## Context

Since the Flex service tier went in across all three LLM steps
([ADR 0036](0036-per-step-model-tier-split-for-cost.md)), a generation job routinely takes
60–90 seconds, up from Precise mode's ~25s worst case ([ADR 0013](0013-async-job-based-generation-invocation.md)).
That's long enough that leaving the progress screen, or the app, while it runs is the normal
thing to do, and the user then has no way to learn the track is ready short of reopening the app
(where the history screen's reconcile pass, [ADR 0025](0025-client-job-reconciler-over-jobtracker-extraction.md),
catches up).

The goal is a notification when a job reaches a terminal state, delivered whether the app is
foregrounded, suspended, or not running. Only the API knows when that happens: the terminal
writes are in `GenerationJobWorkerHostedService.ProcessGenerationJobAsync` (Complete /
CompletedViaFallback / Failed). Cancellation is written by `JobStore.CancelAsync` but is
user-initiated and needs no notification.

At the time of the decision the project was signed with a free personal team, which can't use
the Push Notifications capability. A paid Apple Developer Program membership (£79/yr) was
ordered to unblock this.

## Decision

**The API sends an APNs push when a job reaches Complete, CompletedViaFallback or Failed.**

- The app registers for remote notifications and sends its device token as an optional field on
  the generate request; the token is stored on the `GenerationJob`. There are no user accounts,
  so a per-job token avoids a separate device registry, and the token lives exactly as long as
  the job it notifies about.
- The terminal-state transitions in the worker call a push sender after the `UpdateJob` write.
  The send is fire-and-forget: an APNs failure is logged and never affects the job's state.
- The sender is a small hand-written class over `HttpClient` (HTTP/2) using token-based auth: an
  ES256 JWT signed from the `.p8` key with `ECDsa`, cached and reused for under the 60-minute
  limit. The payload goes through a source-generated `JsonSerializerContext` like the rest of the
  API ([ADR 0007](0007-source-generated-json-for-aot-endpoints.md)).
- The payload carries the `jobId`, so tapping the notification opens that job in the app.
- Debug builds get sandbox tokens and production builds get production tokens, so the APNs host
  (`api.sandbox.push.apple.com` / `api.push.apple.com`) is chosen per environment.

## Alternatives considered

**`beginBackgroundTask` to keep the existing poll loop alive, plus a local notification.**
Rejected. It buys roughly 30 seconds after the app is backgrounded, which covered Precise
mode's ~25s jobs but not 60–90s Flex jobs, and it does nothing once the app is terminated.
`BGAppRefreshTask` is no fix: iOS decides when it runs, often minutes or hours later.

**A background `URLSession` against a server long-poll endpoint, plus a local notification.**
The daemon-owned connection survives suspension and wakes the app on completion, with no paid
membership needed. Rejected as non-standard: background sessions exist for large transfers, and
using one to hold open a status long-poll is a workaround for not having push. Its wake-up
timing is also left to the system, and a force-quit cancels it.

**A third-party APNs library (dotAPNS, CorePush).** Rejected. Neither is known to be Native AOT
safe, and the API is AOT-compiled ([ADR 0008](0008-agent-framework-aot-compatibility.md)). What
they save is JWT signing and one HTTP/2 POST, about 60–80 lines that plain `HttpClient` and `ECDsa`
handle AOT-safely, with no extra dependency.

**Firebase Cloud Messaging or Azure Notification Hubs as a relay.** Rejected. Both add a
third-party service and an SDK on the iOS side as well as the API side, and multi-platform
fan-out and audience targeting aren't needed for one iOS client.

## Consequences

- **Depends on a paid membership.** Push only works while the Apple Developer Program membership
  stays active.
- **A new secret on the Pi.** The `.p8` key, its key ID and the team ID have to be deployed and
  kept out of the repo, alongside the existing API keys.
- **The Pi needs outbound access to Apple.** Tailscale only fronts inbound traffic, and the Pi
  already reaches OpenAI, so this is expected to hold, but it's a new external dependency on the
  job-completion path.
- **Tokens die with the job.** `JobStore` is in memory, so a restart loses pending jobs and their
  tokens together. Nothing outlives a job, so nothing needs cleaning up when APNs reports a dead
  token (`410 Unregistered`).
- **In-app and push paths overlap.** If the progress screen is open when the job completes, the
  poll loop and the push both report it. The app has to decide how to present a foreground push
  (`willPresent`) so the user isn't told twice.
- **Harder to test end to end.** A full check needs a real APNs round trip. Payload handling can
  be exercised with `xcrun simctl push`, and the sender unit-tested against a fake
  `HttpMessageHandler`, but token registration and delivery need a device or an Apple silicon
  simulator.
