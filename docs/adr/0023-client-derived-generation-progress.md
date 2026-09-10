# 0023. Client-derived generation progress copy and a client-side poll timeout

Status: Accepted

## Context

[ADR 0013](0013-async-job-based-generation-invocation.md) settled the invocation model: a
generate request creates a job and returns its id; the client polls `GET /tracks/status/{jobId}`
until it sees a terminal state. It deliberately left three things out of scope that the iOS
progress screen now has to decide.

1. **No stage signal.** `GenerationJobDto` carries only the `JobStates` string
   (`Pending / InProgress / Complete / CompletedViaFallback / Failed`), timestamps and `TrackId`.
   The generation pipeline has seven discrete executors (retrieve style/chords/scales → generate →
   validate ↔ repair → persist) and the validate/repair loop can run up to five attempts, but
   none of that reaches the job entity or the DTO — executors hold no reference to the job store,
   and the worker only writes state at the start and end of a job.
2. **No way to tell "slow" from "stuck".** ADR 0013 explicitly rejected a progress percentage and
   noted that a stuck/timeout state would be "an additional state to design". The job store is
   in-memory with no eviction, so a wedged job stays `InProgress` forever.
3. **Poll-error handling.** ADR 0013 says a missing job is a plain `404` and "the client just
   retries", without saying for how long, or how a transient network failure differs from a
   permanent one.

The screen is a `fullScreenCover` shown the moment a job is accepted; the user watches it until
the track is ready or it fails.

## Decision

**Progress copy is derived on the client from the five job states**, not supplied by the backend:
`Pending → "Queued…"`, `InProgress → "Composing your track…"`, an unrecognised state →
`"Working…"`. Enriching the status endpoint with real pipeline-stage messages ("applying
repairs", "validating") is left as a separate, independent task — it needs the worker to write
stage transitions into the job store as the workflow runs, and it shouldn't block the screen.

**A client-side poll timeout with a `.timedOut` UI state.** `PollingConfiguration` holds a total
poll budget (180s, a constant — well above Precise mode's ~25s worst case per ADR 0013, with
headroom for Vibe mode). If the job hasn't reached a terminal state by then, the screen stops
polling and shows a "taking longer than expected" state that tells the user the job may still
finish server-side. This state exists only on the client; the backend job is untouched.

**Transient-error tolerance.** A failed poll is classified:

- `unauthorized` / `missingAPIKey` / `validation` — give up immediately; the next poll won't
  differ.
- `404` — tolerate one blip, then treat the job as lost (an API restart dropped the in-memory
  job, per ADR 0013).
- everything else (`transport`, `5xx`, `decoding`, and an unexpected `429` — the status endpoint
  is rate-limit-exempt server-side) — transient; keep polling until four *consecutive* failures,
  then give up. A single successful poll resets the count.

The poll interval is a build setting (`STATUS_POLL_INTERVAL` → `StatusPollInterval` Info.plist
key → `PollingConfiguration`), 1s in Debug and 2s otherwise; a missing or malformed value falls
back to 2s rather than being fatal, because a wrong cadence degrades the screen but doesn't break
the app (unlike an unset `APIBaseURL`).

The screen is presented as a `fullScreenCover`, not pushed onto the navigation stack: generation
is a modal interruption of the form, and there is no half-submitted state to swipe back to. It
still has an explicit exit — a toolbar "Cancel" while polling, "Done" once terminal — since a
generation in progress must be abandonable (the pre-cover flow let the user dismiss the
acceptance alert immediately). Cancelling only stops the client polling; the backend job has no
cancel endpoint and may still run to completion.

## Alternatives considered

**Wait for the backend to emit stage messages before building the screen** — rejected. The five
states already support meaningful copy, and the backend change (plumbing stage transitions
through the workflow into the job store) is a self-contained piece of work that gains nothing
from being coupled to the screen. Doing it first would just delay the screen.

**Poll forever** — rejected. A wedged job would leave the screen spinning with no way out but
force-quitting the app.

**Add a stuck/timeout state to the backend** instead of the client — rejected for now. It's more
scope, and ADR 0013's reasoning still holds: for Vibe mode the backend genuinely can't tell
"slow third-party render" from "stuck", so a server-side timeout would be guesswork. A client
timeout is honest about what it is — "I've waited long enough" — and needs no backend change.

**Fail the screen on the first poll error** — rejected. Polling every 1-2s over a cellular link,
a single dropped request is common and self-corrects; surfacing it as a failure would make the
screen feel broken.

**Navigation push instead of `fullScreenCover`** — rejected. A push invites a back-swipe to a
form whose submission is already in flight, and generation isn't a place in the app's
navigation hierarchy — it's a transient state between the form and (eventually) a player screen.

## Consequences

The progress copy is a client-side guess at what the pipeline is doing, keyed only to
`InProgress`. It can't distinguish "generating the first candidate" from "on repair attempt 4",
so the screen will sit on "Composing your track…" for the whole run. When the backend does start
reporting stages, this copy is where it plugs in.

On timeout the screen gives up but the backend job keeps running, completes, and writes its track
to disk — with nowhere for the user to see it. Until a track-library / results screen exists, a
timed-out generation is effectively lost to the user even though it succeeded. This makes the
180s constant a real tuning knob: too low and good jobs get abandoned.

The `.lost` classification assumes a `404` means an API restart. If the endpoint ever returns
`404` for another reason, the screen will mislabel it. Acceptable while `TrackEndpoints.GetStatus`
has exactly one `404` path (`jobStore.TryGet` miss).

The poll timeout and consecutive-failure budget are constants, not build settings — changing them
is a code change. Deliberate: they're client-resilience policy, not environment configuration,
and tests override them via the initializer.
