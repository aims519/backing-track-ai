# backing-track-ai

<!-- TODO: one-paragraph description of the project. -->

For now, see [`docs/backing-track-generator-scope.md`](docs/backing-track-generator-scope.md)
for the full scope and [`docs/adr/`](docs/adr/README.md) for the design decisions.

## Overview

<!-- TODO -->

## Architecture

<!-- TODO: diagram + description. -->

## Repository layout

| Path | What |
| --- | --- |
| [`api/`](api/README.md) | ASP.NET Core backend (Native AOT), deployed to a Raspberry Pi. |
| [`app/`](app/README.md) | Native iOS app (SwiftUI). Project scaffolded, no features yet. |
| [`docs/`](docs/) | Project scope and [Architecture Decision Records](docs/adr/README.md). |
| `api/local-observability/` | Docker Compose stack for running the OpenTelemetry backend locally. |
| `scripts/` | Pi bootstrap and the local Native AOT / trimming check. |

## Status

<!-- TODO -->

## Building

Requires the .NET 10 SDK (pinned in [`global.json`](global.json)).

```bash
dotnet build api/BackingTrack.sln
dotnet test  api/BackingTrack.sln
```

See [`api/README.md`](api/README.md) for local configuration and deployment.

Requires Xcode 26+ and [XcodeGen](https://github.com/yonaskolb/XcodeGen) for the iOS app — see
[`app/README.md`](app/README.md).

## License

[MIT](LICENSE).
