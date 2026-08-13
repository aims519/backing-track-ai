# 0014. Local filesystem storage for generated tracks, keyed by trackId

Status: Accepted

## Context

Completed generation jobs need somewhere to put the resulting audio file, and the API needs a
`/tracks/download/{id}` endpoint to serve it back to the iOS client. `GenerationJob` already
carries a `TrackId` distinct from the job's own `Id`, plus an unused `AudioLocation` field,
anticipating this.

Two things this decision has to work around:

- The job store is in-memory only (ADR 0013) — a process restart loses all job state, and that's
  an accepted failure mode for job status. Download shouldn't have to inherit that same fragility
  if the file itself is still sitting on disk.
- No third-party audio-generation provider has been chosen yet (Vibe mode is still `e.g.
  ElevenLabs Music, Stable Audio` in the scope doc), and those candidates don't share an output
  format — ElevenLabs Music defaults to mp3-family output, Stable Audio typically returns wav. A
  future stretch goal (server-side FluidSynth export for Precise mode) will add a second,
  independent output format. Picking one fixed extension for all tracks now would be a guess, and
  guessing wrong later means either a transcode step or a data migration.

## Decision

- **Storage location**: local filesystem on the Pi, under a configurable `StorageSettings.TracksDirectory`
  (set per environment via `appsettings.*.json`, same pattern as `Auth`/`Otel`/`RateLimit`), living
  on the USB-SSD-backed path rather than the SD card, per the scope doc's storage-reliability note.
- **Filename**: `{trackId}.{ext}`. `ext` is a per-mode config value (e.g. one setting for Vibe,
  one for the future Precise export), not hardcoded — each mode's provider/pipeline decides its
  own native format, avoiding an unnecessary transcoding step on the Pi.
- **Download endpoint** (`GET /tracks/download/{trackId:guid}`): looks the file up directly on
  disk by globbing `{TracksDirectory}/{trackId}.*` and serving whichever single file matches — it
  does not consult `JobStore` at all. `Content-Type` is derived from the matched extension via a
  small extension-to-content-type map. No match → `404`, consistent with the existing "missing
  resource is a 404, not a domain state" handling in `GetStatus`.
- `AudioLocation` is dropped from `GenerationJob` and `GenerationJobDto` — it would only ever
  duplicate a path the client can already derive from `TrackId`, and exposing a server filesystem
  path to the client had no purpose once download doesn't need one.

## Alternatives considered

- **Cloud object storage** (S3, Cloudflare R2, Backblaze B2) — rejected. It would offload
  durability/reliability concerns from the Pi's disk, but the scope doc's own differentiation
  section names "end-to-end self-hosted deployment on personal hardware, not a cloud SaaS" as a
  deliberate selling point of this project; reaching for a cloud backend for the app's one piece
  of persistent state would undercut that story, for a single-user app where "backups are
  explicitly out of scope for now" is already an accepted risk.
- **Keying the download lookup by jobId, via `JobStore`** — rejected. It would tie the file's
  availability to the in-memory job store's lifetime, so a process restart would strand any file
  still on disk behind a `JobStore` miss — a worse failure mode than the one ADR 0013 already
  accepts for status polling, not an equivalent one.
- **A single fixed extension for all tracks (e.g. always `.mp3`)** — rejected. No provider has
  been chosen for Vibe mode yet, and the two realistic candidates return different native formats;
  Precise mode's future FluidSynth export is a separate pipeline likely to produce `.wav`. Fixing
  one extension now would force a transcode step (and its own dependency, e.g. ffmpeg on the Pi)
  for no reason beyond premature commitment.
- **A metadata sidecar file (or DB row) recording each track's extension/content-type** — rejected
  as unnecessary. A `Guid` filename has no collision risk, so globbing `{trackId}.*` finds the one
  file that can possibly match, without needing a second source of truth to keep in sync.

## Consequences

Download is decoupled from `JobStore`'s lifetime — a track file survives a process restart even
though its job's status does not, which is a strictly better outcome than coupling the two would
have given.

Adding a new mode or changing a mode's output format later is a config change (a new/updated
extension setting), not a rework of the lookup path, since the glob doesn't hardcode an extension.

No cleanup/retention policy exists for track files — disk usage on the Pi will grow unbounded
over time. Same "explicitly out of scope for now" reasoning as backups elsewhere in the scope doc;
worth its own decision once it actually matters, not before.

Because the lookup relies on globbing rather than an exact filename, a `TracksDirectory` that ever
accumulated more than one file per `trackId` (shouldn't happen given `Guid` naming, but e.g. via a
manual file dropped in for debugging) would make the "serve the single match" assumption wrong;
this isn't guarded against.
