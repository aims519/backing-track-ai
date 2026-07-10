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
