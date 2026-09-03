# 0024. Client-side SwiftData job/track history, mirrored from the poll loop

Status: Accepted

## Context

Until now the iOS app held a generation job only in memory. Once
`GenerationProgressView` was dismissed, the job and its result were gone — the only
way back was to re-poll `GET /tracks/status/{jobId}`, which needs the backend still
holding the job in its in-memory `JobStore` ([ADR 0013](0013-async-job-based-generation-invocation.md))
*and* reachable over Tailscale ([ADR 0022](0022-plaintext-http-over-tailscale-ats-disabled.md)).

Two gaps made a local store necessary now:

- [ADR 0023](0023-client-derived-generation-progress.md) introduced a client poll
  timeout, and called out that on timeout "the backend job keeps running,
  completes, and writes its track to disk — with nowhere for the user to see it …
  a timed-out generation is effectively lost to the user even though it
  succeeded." The same is true for a job dropped by an API restart (the `.lost`
  classification).
- The "Design job list / history screen" task is blocked without a durable list of
  past jobs to render.

The confirmed design intent for the project is stronger than caching: once a
completed track's data has come back from the API, the client should own it
outright and play it back with no dependency on the backend or Tailscale. That
requires the data to live on the device.

The scope doc anticipated persistence only as a stretch goal, and framed it as
*server-side* SQLite on the Pi ("Saved presets / recent tracks (SQLite on the Pi
— no login needed for single-user use)"). It also fixed "no user accounts" as a
Non-Goal, with the note that a `user_id` column should stay a cheap addition
later.

Constraints in play: the app targets iOS 18, so SwiftData is fully available; it
uses `SWIFT_STRICT_CONCURRENCY: complete`; and it has no dependency-injection or
environment plumbing — every screen owns an `@Observable` model constructed with
defaulted arguments, following the `APIKeyStoring` / `KeychainAPIKeyStore` pattern
([`Networking/APIKeyStore.swift`](../../app/BackingTrack/Networking/APIKeyStore.swift)).

## Decision

**A local SwiftData store, written through from the existing poll loop.**

- One `@Model`, `JobHistoryEntry` — a job produces at most one track, so a split
  Job/Track pair with a relationship earns nothing. It carries the request
  parameters (captured at submit — `style` in particular, which the job DTO
  doesn't echo back), the last-seen status, the client's *disposition*, the
  server timestamps and `trackId`, and nullable fields reserved for cached track
  content that this change does not populate.
- Status is stored as the backend's wire string (`JobStatus.wireValue`), not an
  encoded enum, so `JobStatus.unknown("…")` round-trips its captured value.
- `JobDisposition` (`inFlight / ready / serverFailed / timedOut / lost /
  pollError`) is a flattened, persistable form of `GenerationProgressModel.Phase`
  + `.Failure`. Persisting `.timedOut` / `.lost` is what closes the ADR 0023 gap.
- A `JobHistoryStore` protocol (`@MainActor`, **not** `Sendable` — unlike the API
  client it never crosses an actor boundary) with a SwiftData-backed
  implementation and an injected default, mirroring `APIKeyStoring`. The
  `@Observable` models call it; `GenerateTrackModel` writes a row the moment the
  API accepts a job, and `GenerationProgressModel` funnels every `phase` change
  through one `transition(to:)` that also records the disposition.
- The poll loop is otherwise unchanged: in-memory state stays the source of truth
  for the live screen, and the store is a durable mirror. A store write failure is
  logged (debug only) and swallowed — it must never fail a generation, in any
  configuration.
- Reads for the future history screen go through `@Query` (the container is placed
  in the environment by `BackingTrackApp` for exactly this); the store stays the
  write path.
- Keep everything: no retention or cleanup policy, matching ADR 0014's stance for
  server-side track files.
- Reads inside the store (`entry(jobID:)` — the upsert/update hot path) fetch every
  row and filter in Swift rather than using a `#Predicate`: string-equality
  predicates against SwiftData's in-memory store trapped, and the table is a single
  user's own generations. The history *screen* still uses `@Query` for its list.
- The shared container is resolved lazily inside `SwiftDataJobHistoryStore` — every
  model takes one as a default init argument, and most only to satisfy a
  form-logic test, so constructing the store must not open SwiftData.
- If the container can't open its store — most likely an unmigratable schema, since
  there is no `SchemaMigrationPlan` yet — it deletes the store and rebuilds empty;
  if even that fails it falls back to an in-memory container for the session. It
  never crashes the app over persistence.
- IDs are stored as `String` (`uuidString`). Local-only; no CloudKit.

## Alternatives considered

**Server-side SQLite on the Pi** (the original scope-doc framing) — rejected. The
client has to hold the track data locally to meet the offline-playback goal
anyway, so a server copy would be redundant work for MVP, and it would couple the
history screen to Tailscale reachability, which is the dependency this change
exists to remove. The scope doc's `user_id`-column note still applies if history
ever needs to sync across devices.

**Core Data** — rejected. More boilerplate (a `.xcdatamodeld`, `NSManagedObject`
subclasses or codegen) for no capability gain at an iOS 18 floor, and less
idiomatic for an owner deliberately learning current SwiftUI-era APIs.

**Plain `Codable` files on disk** — viable given how little data there is, but it
hand-rolls the querying, sorting and schema-evolution that SwiftData gives for
free, and offers nothing for the eventual cached-audio blobs. Rejected as a false
economy.

**`@Query` everywhere, no store abstraction** — rejected. The write path
originates in `@Observable` models, not views, so they need a context handle that
isn't tied to being inside a SwiftUI `body`; and routing writes through `@Query`
would force a SwiftData dependency into every model unit test instead of a simple
fake.

**Split `Job` + `Track` `@Model` with a relationship** — rejected. A to-one
relationship with no query that benefits from the split, at the cost of two types
and the relationship's lifecycle rules.

**Download and cache the track content as part of this change** — rejected for
now. There is no download method on the API client and nothing that decodes the
precise-mode MIDI JSON yet; that logic belongs with the player screen, which owns
playback. Committing now to how content is stored (an external-storage blob vs. a
file reference) would pre-empt a decision the player screen is better placed to
make. The schema already carries the nullable fields, so adding the *write path*
later needs no migration at all — but note that moving `preciseContentData` to
`@Attribute(.externalStorage)` at that point *is* a storage-option change, which
without a `SchemaMigrationPlan` fails to open and hits the wipe-and-rebuild path.
The player-screen task will need to add a migration plan alongside it.

**`@Attribute(.unique)` on the job id** — dropped. It traps against the
in-memory store the tests use. `JobHistoryStore` enforces uniqueness by fetching
before inserting instead — non-atomic, but safe because every write is serialised
on the main actor. A future `ModelActor` (see Consequences) would break that and
would need the constraint back, or a different guard.

**Repository as the single source of truth for the live screen** — rejected. It
would couple the progress screen's correctness to a store write succeeding; the
mirror approach keeps the screen working even if persistence fails.

## Consequences

- Local history grows without bound. Accepted: the rows are tiny, and the real
  cost (cached content blobs) isn't incurred yet. A `prune(...)` hook on
  `JobHistoryStore` is the place to revisit it.
- A swallowed store-write failure means that job may be missing from history, with
  only a debug `print` to signal it. The alternative — failing the generation
  because persistence hiccuped — is worse. `GenerationProgressModel` re-runs the
  (idempotent) insert when polling starts, so a failure at submit time still
  self-heals as long as the screen opens.
- `JobHistoryEntry` is not `Sendable` and its context is main-actor-bound. A future
  background or bulk write needs a `ModelActor` behind the same `JobHistoryStore`
  protocol, not a direct context hop.
- With no `SchemaMigrationPlan`, any non-additive schema change discards existing
  history (the container rebuilds empty). Fine while there is no real data to lose;
  a genuine migration plan is needed before that stops being true.
- The history screen is now unblocked, and should read via `@Query` against the
  container `BackingTrackApp` publishes.
- The store is only ever written from the client's view of the job. Dismissing the
  progress screen before a terminal state is the *normal* user action, not an edge
  case — so most rows will sit at `.inFlight` with a stale status. **The history
  screen must reconcile on open**: re-poll every non-terminal row's job and write
  the result through. That's required follow-up, not a nicety.
- The write-through lives inside `GenerationProgressModel`, which already owns the
  poll loop, the timeout policy and the error classification (ADR 0023). That's a
  lot of non-presentation responsibility on a view-model, and there's no service
  layer between the view-models and the `APIClient` / `JobHistoryStore`. Left as-is
  here to keep the diff to "add persistence" rather than reshaping accepted code.
  **Anticipated refactor:** extract a `JobTracker` that owns polling + timeout +
  classification + persistence and exposes the job's state as an `AsyncStream`;
  `GenerationProgressModel` then only maps that to `Phase`. The history-screen task
  is the right time — its reconcile-on-open pass is the second caller of "poll a
  job and persist the result", so the interface can be designed against two real
  consumers instead of guessed at now.
