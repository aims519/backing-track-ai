# BackingTrack API

ASP.NET Core backend, targeting Native AOT for deployment on a Raspberry Pi. See
[`/docs/backing-track-generator-scope.md`](../docs/backing-track-generator-scope.md) for the full project scope, and [`/docs/adr/`](../docs/adr/) for the reasoning behind non-obvious structural decisions.

## Structure

```text
api/
  src/
    BackingTrack.Api/          host project — endpoints, auth, rate limiting, telemetry, DI wiring
    BackingTrack.Core/         shared settings classes (Config/) bound from configuration
    BackingTrack.Generation/   generation pipeline
      Workflows/                 workflow builder and executors
      Steps/                     retrieval, generation, validation, repair, critic, fallback, persistence
      KnowledgeBase/             RAG knowledge base: loading, embeddings, scale matching
      Jobs/                      job model, store, queue, worker and eviction services
      Notifications/             APNs push on terminal job state
  tests/
    BackingTrack.Api.Tests/                    endpoint, auth, rate-limit and job-lifecycle tests
    BackingTrack.Generation.Tests/
    BackingTrack.Generation.Eval.Tests/
    BackingTrack.KnowledgeBase.Embedder.Tests/
  tools/
    BackingTrack.KnowledgeBase.Embedder/   offline: build the RAG vector store
  benchmarks/
    BackingTrack.Generation.Benchmarks/
  eval/
    BackingTrack.Generation.Eval/           end-to-end generation eval harness — see eval/README.md
    BackingTrack.Critic.Eval/               critic-in-isolation eval — see its README.md
    run-model-comparison.sh                 runs the harness across models
  local-observability/                      docker-compose Grafana + OTel collector for local telemetry
```

## Endpoints

Everything under `/tracks` needs an `X-API-Key` header and returns `401` without a valid one. `generate`
is rate limited per key, and the limiter runs before the key check, so an over-limit request gets `429`
whatever its key. The other three endpoints are exempt so polling doesn't eat the budget.

| Method | Path | Success | Other responses |
|---|---|---|---|
| `POST` | `/tracks/generate` | `202 Accepted` with the job and a `Location` header pointing at its status | `400` validation problem details; `429` rate limited; `503` with `Retry-After` if the job queue is unavailable |
| `GET` | `/tracks/status/{jobId}` | `200` with the job's current state and feedback | `404` unknown job |
| `GET` | `/tracks/download/{trackId}` | `200` with the stored track file (range requests supported) | `404` unknown track |
| `POST` | `/tracks/cancel/{jobId}` | `204` | `404` unknown job |

`/healthz/live` and `/healthz/ready` are the liveness and readiness probes, outside the key check.

## Conventions

- Shared build settings (target framework, nullable, implicit usings) live in `Directory.Build.props`, not per-project `.csproj` files.
- Style rules are enforced via `.editorconfig` (standard .NET conventions) and `EnforceCodeStyleInBuild`.
- `dotnet build` should be warning-free; pin any transitively-flagged NuGet package explicitly rather than suppressing the advisory.

## Building

```bash
dotnet build
```

## Versioning

The API's version comes from git tags, not from the project files. [MinVer](https://github.com/adamralph/minver)
reads the nearest `api-v*` tag ([ADR 0046](../docs/adr/0046-minver-from-git-tags-for-api-version.md)):
on a tagged commit the version is the tag (`0.1.6`), and `n` commits later it is `0.1.7-alpha.0.n`. The
deploy workflow appends the exact commit SHA, and the result is reported as the OpenTelemetry
`service.version`, so a trace or error can be traced to the deploy that produced it.

There is no `<Version>` in the project files. The maintainer cuts a release by tagging the merge commit
on `main` straight after merging. The deploy's `publish` job reads tags when it checks out the commit,
and `verify` runs first, so a tag pushed in time is baked into that deploy. Tagging doesn't trigger a
deploy itself, so a later tag leaves the running service on its pre-release version string
(`x.y.z-alpha.0.n`) until the next push to `main`. The `api-v` prefix keeps these tags separate from any
future `ios-v` tags.

## Configuration

Settings classes live in `BackingTrack.Core/Config/`. `appsettings.json` holds the defaults shared by
every environment (`Critic`, `Apns`, `JobEviction`); `appsettings.Development.json` and
`appsettings.Production.json` hold the rest. Secrets are placeholders in those files
(`FROM_USER_SECRETS` in Development, `FROM_ENV` in Production) and are supplied separately.

| Section | What it controls |
|---|---|
| `Auth` | `ApiKey`, the value `X-API-Key` is checked against. **Secret.** |
| `OpenAi` | Base URL and API key (**secret**), plus model, API (`Responses` or `ChatCompletions`), service tier and max output tokens for each of the generate, repair and critic steps |
| `VoyageAi` | API key (**secret**), base URL and embedding model, used to embed each request's style text for retrieval (chords and scales are matched deterministically) |
| `Critic` | `PassingScoreThreshold` (0–100) and `MaxAttempts` for the critic's pass/fail gate |
| `TrackGenerationWorkflow` | `GenerationMaxAttempts`, the cap on generate-plus-repair attempts per pass. Exhausting it, running out of `Critic:MaxAttempts`, or an error in a step falls back to the rule-based generator |
| `TrackStorage` | `BaseFolderPath`, where generated tracks are written and served from |
| `RateLimit` | `PermitLimit` requests per `WindowSeconds`, per API key |
| `JobEviction` | `SweepIntervalSeconds` and `JobTtlSeconds` for dropping finished jobs from the in-memory store |
| `Apns` | Push notification server and bundle ID. The signing key (`P8KeyId`, `P8Key`, `TeamId`) is optional **secret**; without it push is disabled and the job still completes |
| `Otel` | Telemetry export. On in Development, expecting a collector on `localhost:4317` (start `local-observability/docker-compose.yml`, or set `Otel:IsEnabled` to `false`) |

`Auth`, `OpenAi`, `VoyageAi`, `Critic`, `TrackStorage` and `JobEviction` are validated at startup, so a
missing or out-of-range value fails fast instead of defaulting silently. `RateLimit`,
`TrackGenerationWorkflow` and `Apns` are not, so check their values by hand.

The knowledge base's vector files are committed (`BackingTrack.Generation/KnowledgeBase/Content/`), so
you only need the embedder tool if you change the knowledge base content.

## Local development

`/tracks` endpoints require an `X-API-Key` header, checked against `Auth:ApiKey` in configuration
(bound to `AuthSettings`). To generate tracks end to end you need three secrets. Set them locally via
user secrets rather than `appsettings.Development.json`:

```bash
cd src/BackingTrack.Api
dotnet user-secrets set "Auth:ApiKey" "<any local value>"
dotnet user-secrets set "OpenAi:ApiKey" "<your OpenAI key>"
dotnet user-secrets set "VoyageAi:ApiKey" "<your Voyage AI key>"
```

On the Pi, the same settings are supplied as environment variables in `/opt/backingtrack/shared/api.env`
(double underscore: `Auth__ApiKey`, `OpenAi__ApiKey`, `VoyageAi__ApiKey`, and optionally `Apns__P8KeyId`,
`Apns__P8Key` and `Apns__TeamId`). [`scripts/pi-bootstrap.sh`](../scripts/pi-bootstrap.sh) creates the file
with only the ASP.NET Core environment and URL settings; the secrets are added by hand.

[`src/BackingTrack.Api/BackingTrack.Api.http`](src/BackingTrack.Api/BackingTrack.Api.http) has requests for the
endpoints, but deliberately doesn't define `{{HostAddress}}` or `{{ApiKey}}` inline, to keep the key out of a
tracked file. Create a gitignored `http-client.private.env.json` alongside it:

```json
{
  "dev": {
    "HostAddress": "http://localhost:5198",
    "ApiKey": "<value matching your user secret>"
  }
}
```

Select the `dev` environment when sending requests from the `.http` file.

## Deployment

Push to `main` (touching `api/**`) triggers [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml):

1. **Verify:** re-runs the build, test, format and AOT checks from [`verify.yml`](../.github/workflows/verify.yml)
   against the exact commit being deployed. Nothing else starts unless this passes.
2. **Publish:** builds Native AOT for `linux-arm64`.
3. **Deploy:** copies the build to `/opt/backingtrack/releases/<sha>` on the Pi over SSH, repoints the
   `current` symlink at it and restarts the `backingtrack-api` systemd service.
4. **Check:** polls `/healthz/live` and `/healthz/ready`.
5. **Roll back or prune:** if any step after the swap fails, the previous release is restored and the
   service restarted, and the run still fails ([ADR 0047](../docs/adr/0047-automatic-rollback-and-release-pruning-on-the-pi.md)).
   If both checks pass, [`scripts/prune-releases.sh`](../scripts/prune-releases.sh) keeps the five newest
   releases, plus the live one if it isn't among them, and deletes the rest.

Rollback goes back one release and restores only the binary, not any data the failed release wrote. The first
deploy has nothing to fall back to. When Otel is enabled, `/healthz/ready` is just the collector check, so a
collector outage during a deploy also triggers a rollback.

This assumes the Pi has already been bootstrapped once — see
[`/scripts/pi-bootstrap.sh`](../scripts/pi-bootstrap.sh) (creates the runtime/deploy users,
systemd unit, and directory structure) and
[`docs/adr/0002-tailscale-oauth-shared-deploy-user.md`](../docs/adr/0002-tailscale-oauth-shared-deploy-user.md)
for the reasoning behind the user/auth model. Bootstrapping and the one-time Tailscale OAuth
client/ACL setup are manual, run-once steps — not part of the CI workflow.
