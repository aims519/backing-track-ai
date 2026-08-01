# 0010. OpenTelemetry tracing and metrics, verified against Native AOT

Status: Accepted

## Context

Following ADR 0009 (built-in `ILogger` + JSON console logging over Serilog), the next step was
adding distributed tracing and metrics, and folding structured logging into the same pipeline —
the reason ADR 0009 gave for not adopting Serilog was specifically that OpenTelemetry would
integrate natively with `Microsoft.Extensions.Logging` once added. `PublishAot=true` is already
load-bearing (ADR 0007, ADR 0008), so the same diligence applied there — checking real
AOT-compatibility evidence, not trusting docs — needed to apply to the OpenTelemetry packages
before committing to them.

Compatibility research, done directly against the package source and issue trackers rather than
blog posts:

- `OpenTelemetry.Instrumentation.AspNetCore` and `OpenTelemetry.Instrumentation.Http`
  (`opentelemetry-dotnet-contrib`) both declare `IsAotCompatible=true` in their own `.csproj`
  files. The core `OpenTelemetry` SDK (`opentelemetry-dotnet`) sets `IsAotCompatible=true`
  repo-wide by default for any `net8.0`+ target via `build/Common.prod.props` — the same pattern
  ADR 0008 relied on for Agent Framework.
- Both issue trackers show a deliberate, tracked AOT effort (`[Infra] Set IsAotCompatible where
  relevant` #4040, `Extend AoT support` #1541 in opentelemetry-dotnet-contrib) with multiple
  historical AOT bugs found and closed (`PropertyFetcher` made AOT-safe, `StackExchangeRedis`
  and `AWSLambda` AOT warnings fixed) — evidence of real testing, not just a flag.
- The only open AOT-tagged issues found (opentelemetry-dotnet #4725, #6380) are about adopting
  the .NET config-binder source generator internally, not reports of breakage. Nothing open
  flags `OpenTelemetry.Instrumentation.AspNetCore` or `.Http` as broken under Native AOT.
- Unlike ADR 0008's Agent Framework finding, no known-and-fixed *runtime-only* AOT bug (the
  `CheckpointManager.CreateJson` equivalent) surfaced for these specific packages during
  research — either a cleaner AOT story here, or a narrower risk surface than an agent
  framework's. Either way, per the same caveat ADR 0008 raised, an analyzer-clean build is not
  proof by itself.

## Decision

Add `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, and
`OpenTelemetry.Instrumentation.Http`, wired via `AddOpenTelemetry().WithTracing()/WithMetrics()`,
plus `builder.Logging.AddOpenTelemetry()` to route existing structured logs through the same
pipeline. All export via OTLP (`AddOtlpExporter`) rather than the `Console` exporter package
used only for initial manual verification — `OpenTelemetry.Exporter.Console` was removed once a
real destination existed.

The whole block is gated behind an `Otel:bEnabled` config flag (bound via the project's existing
source-generated `Microsoft.Extensions.Configuration.Binder.SourceGeneration` binder, confirmed
AOT-safe — not the reflection-based `ConfigurationBinder` that would otherwise be an AOT risk),
so nothing OpenTelemetry-related is registered at all when disabled. `Otel:CollectorEndpoint`
is required when enabled, validated with an explicit guard clause rather than a suppressed
nullable warning.

For local development, a collector + `grafana/otel-lgtm` (bundled Grafana + Loki + Tempo +
Prometheus) stack lives in `api/local-observability/`, run via `docker compose`. The collector
sits in front of the all-in-one image rather than the app talking to it directly, so the
pipeline (batching, potential future fan-out to multiple backends) matches what a real
deployment would look like, not just a dev shortcut.

Verified per the ADR 0008 standard: `dotnet publish -r osx-arm64 -c Release` completed with zero
trim/AOT warnings, and — the actual proof, not just a clean analyzer run — the *published AOT
binary* was run directly (`ASPNETCORE_ENVIRONMENT=Development ./BackingTrack.Api`) and confirmed
to produce real traces, metrics, and logs visible in Grafana, not just under `dotnet run`.

## Alternatives considered

An all-Grafana stack (Tempo/Loki/Prometheus/Grafana) was chosen over Jaeger for local
visualization — considered running both side by side, rejected as the same duplicate-pipeline
problem ADR 0009 already ruled out for Serilog: two trace backends receiving the same data long
-term is pure overhead, and the pre-wired `grafana/otel-lgtm` image gives one integrated UI
(trace-to-logs-to-metrics correlation) that four separately-wired containers wouldn't
automatically provide.

## Consequences

The `Otel:bEnabled` flag means telemetry is off by default in Production
(`appsettings.Production.json` currently has `bEnabled: false`, `CollectorEndpoint: "TBD"`)
until the Pi has somewhere to actually send data — this is a deliberate placeholder, not an
oversight, and needs revisiting once a Pi-side collector or remote OTLP endpoint exists.

Any future dependency that emits its own telemetry (an HTTP client, a database driver, an agent
framework tool) needs the same AOT-compatibility check before being added — this is now the
established pattern (ADR 0008, ADR 0009, this ADR), not a one-off. Local verification requires the `api/local-observability/` Docker Compose stack to be running;
without it, `Otel:bEnabled: true` locally won't fail startup (the OTLP exporter fails silently
in the background rather than throwing), so an unnoticed missing `docker compose up` reads as
"telemetry isn't showing up" rather than a clear error — worth remembering when debugging.
