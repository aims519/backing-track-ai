# BackingTrack API

ASP.NET Core backend, targeting Native AOT for deployment on a Raspberry Pi. See
[`/docs/backing-track-generator-scope.md`](../docs/backing-track-generator-scope.md) for the full project scope,
and [`/docs/adr/`](../docs/adr/) for the reasoning behind non-obvious structural decisions.

## Structure

```text
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

```bash
dotnet build
```

## Local development

`/tracks` endpoints require an `X-API-Key` header, checked against `Auth:ApiKey` in configuration
(bound to `AuthSettings`). Set it locally via user secrets rather than `appsettings.Development.json`:

```bash
cd src/BackingTrack.Api
dotnet user-secrets set "Auth:ApiKey" "<any local value>"
```

On the Pi, the same setting is supplied as the `Auth__ApiKey` environment variable in
`/opt/backingtrack/shared/api.env` (double underscore — see
[`scripts/pi-bootstrap.sh`](../scripts/pi-bootstrap.sh)).

`BackingTrack.Api.http` requests reference `{{ApiKey}}` but deliberately don't define it inline, to
keep the key out of a tracked file. Create a gitignored `http-client.private.env.json` alongside it:

```json
{
  "dev": {
    "ApiKey": "<value matching your user secret>"
  }
}
```

Select the `dev` environment when sending requests from the `.http` file.

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
