# 0046. Version the API with MinVer from git tags, with the commit SHA as build metadata

Status: Accepted

## Context

[ADR 0010](0010-opentelemetry-tracing-and-metrics.md) tags every span and metric with
`service.version`, which was read from `Assembly.GetName().Version`. Nothing in `api/` set a
`<Version>`, so every deploy reported `1.0.0.0` and Grafana could not say which deploy produced a
trace or an error.

The API is deployed to the Pi on every push to `main` that touches `api/**`
(`.github/workflows/deploy.yml`), so the commit is what identifies a deploy. The same version
should also say something a person can read and order: which release is this, and is it newer
than the one before.

## Decision

Reference [MinVer](https://github.com/adamralph/minver) from `BackingTrack.Api` (build-time only,
`PrivateAssets="all"`, tag prefix `v`). The version comes from the nearest `v*` tag on the
commit's history:

- on a tagged commit: `1.2.0`
- `n` commits after a tag: `1.2.1-alpha.0.n`
- before the first tag exists: `0.0.0-alpha.0.n`

The .NET SDK appends the commit as build metadata (`+<sha>`) to the informational version.
`deploy.yml` also passes `-p:SourceRevisionId=${{ github.sha }}` so the suffix is the exact commit
being deployed rather than whatever the SDK finds in the container's `.git`.

`BuildInfo.Version` reads `AssemblyInformationalVersionAttribute` and is what the OpenTelemetry
resource reports as `service.version`. A release is cut by pushing a `vMAJOR.MINOR.PATCH` tag.

## Alternatives considered

- **`SourceRevisionId` alone (`1.0.0+<sha>`).** Identifies the deploy precisely but has no
  ordering and no release number: two SHAs can't be compared, and the `1.0.0` prefix never
  changes unless someone edits the project by hand.
- **CI run number (`1.0.<run_number>+<sha>`).** Monotonic, but it counts every run of
  `deploy.yml` including ones where `verify` failed and nothing shipped, so the numbers that
  reach the Pi have gaps; a re-run keeps its number; and the counter restarts if the workflow
  file is recreated. It orders deploys but is not a release number.
- **Hand-edited `<Version>` in the csproj.** The iOS-style manual bump, but the iOS app needs it
  because TestFlight rejects non-increasing versions. Nothing consumes the API's version that
  way, so it would only go stale between bumps.
- **Nerdbank.GitVersioning / GitVersion.** Both do the job. They need a `version.json` or a
  `GitVersion.yml` and carry far more configuration than a single-service repo needs; MinVer
  needs one property.

## Consequences

- CI needs full history and tags. `verify.yml` and the deploy `publish` job use `fetch-depth: 0`,
  and mark the bind-mounted `/workspace` as a git `safe.directory` inside the build container,
  because MinVer shells out to `git` and the checkout is owned by a different uid.
- The `mcr.microsoft.com/dotnet/sdk:10.0` image has to keep shipping `git`.
- No tag exists yet, so every build reads `0.0.0-alpha.0.<height>` until the first `v*` tag is
  pushed. Tag the first release before relying on the number.
- The version is fixed when the deploy builds. Pushing a tag onto a commit that has already
  been deployed does not relabel the running binary; it appears at the next build, for example
  by re-running the deploy workflow once the tag is pushed.
- Releases are a manual step (push a tag), and the discipline of when to tag, and what counts
  as a major, minor or patch bump for a service with no external API consumers, is the
  owner's call.
