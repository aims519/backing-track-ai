# 0009. Built-in ILogger + JSON console formatter over Serilog

Status: Accepted

## Context

The API needed structured logging: `ILogger<T>` was already in use ad hoc (e.g.
`GlobalExceptionHandler`), but console output wasn't structured or consistently formatted.
Two constraints shaped the choice: `PublishAot=true` is already load-bearing (ADR 0007, ADR
0008), and OpenTelemetry tracing and metrics are planned as a near-term follow-up, alongside
API-key-correlated request logging.

## Decision

Use the built-in `Microsoft.Extensions.Logging` pipeline: `builder.Logging.AddJsonConsole(...)`
in Production for machine-parseable structured output, `AddSimpleConsole(...)` in Development
for human-readable local output. No third-party logging package added.

`AddJsonConsole` ships in the ASP.NET Core shared framework (no extra NuGet dependency) and is
AOT-safe by construction — the formatter writes known fields (message, level, category,
exception, structured state key/values) directly via `Utf8JsonWriter` rather than through
reflection-based `JsonSerializer`, so it needs no `JsonSerializerContext` registration.

On the Pi, systemd/journald captures stdout automatically, so the JSON lines are already queryable
today via `journalctl -u <service> -o cat | jq`, with no additional shipping infrastructure
needed yet.

## Alternatives considered

Serilog — rejected. It has a richer sink/enricher ecosystem, but many sinks and enrichers aren't
trim-annotated, which risks fighting AOT warnings on a project that already commits to
`PublishAot=true`. More importantly, the planned OpenTelemetry work makes it redundant: the
OTel .NET SDK integrates natively with `Microsoft.Extensions.Logging`
(`builder.Logging.AddOpenTelemetry()`) and will share the same trace/span context automatically
once tracing is added, giving API-key-correlated request logging without a second logging
pipeline. Adding Serilog now would mean running two overlapping systems (Serilog for logs, OTel
SDK for traces/metrics) instead of one.

## Consequences

Correlating a request's logs currently requires no dedicated correlation-id middleware — that
was considered and deliberately deferred, since `Activity.TraceId` will provide this for free
once OpenTelemetry tracing is added, and building a bespoke correlation mechanism now would
likely be thrown away then. API-key-correlated request logging (tagging log lines with which
caller made the request) is left for later, added as a `logger.BeginScope` in `ApiKeyFilter`
once needed — the mechanism doesn't require any new library, just the existing `ILogger`.

The Development/Production formatter split means local console output is never quite what
Production sees; verifying JSON shape (e.g. before wiring a log collector) requires temporarily
switching the Development branch to `AddJsonConsole` rather than trusting local output as-is.
