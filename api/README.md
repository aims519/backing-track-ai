# BackingTrack API

ASP.NET Core backend, targeting Native AOT for deployment on a Raspberry Pi. See
[`/docs/backing-track-generator-scope.md`](../docs/backing-track-generator-scope.md) for the full project scope,
and [`/docs/adr/`](../docs/adr/) for the reasoning behind non-obvious structural decisions.

## Structure

```
api/
  src/
    BackingTrack.Api/          host project — endpoints, DI wiring, config
    BackingTrack.Core/         shared domain models
    BackingTrack.Generation/   track generation logic (LLM prompting, validation, fallback, vibe mode)
  tests/
    BackingTrack.Generation.Tests/
```

Currently a bare template — no generation logic yet.

## Conventions

- Shared build settings (target framework, nullable, implicit usings) live in `Directory.Build.props`, not per-project `.csproj` files.
- Style rules are enforced via `.editorconfig` (standard .NET conventions) and `EnforceCodeStyleInBuild`.
- `dotnet build` should be warning-free; pin any transitively-flagged NuGet package explicitly rather than suppressing the advisory.

## Building

```
dotnet build
```

## Deployment

Push to `main` (touching `api/**`) triggers [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml):
cross-compiles Native AOT for `linux-arm64`, joins the tailnet via `tailscale/github-action`,
and deploys to the Pi over SSH, restarting the `backingtrack-api` systemd service.

This assumes the Pi has already been bootstrapped once — see
[`/scripts/pi-bootstrap.sh`](../scripts/pi-bootstrap.sh) (creates the runtime/deploy users,
systemd unit, and directory structure) and
[`docs/adr/0002-tailscale-oauth-shared-deploy-user.md`](../docs/adr/0002-tailscale-oauth-shared-deploy-user.md)
for the reasoning behind the user/auth model. Bootstrapping and the one-time Tailscale OAuth
client/ACL setup are manual, run-once steps — not part of the CI workflow.
