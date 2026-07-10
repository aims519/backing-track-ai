# 0007. Use source-generated JSON serialization for minimal API responses

Status: Accepted

## Context

The first successful deploy to the Pi (after fixing ADRs 0005 and 0006, plus two bootstrap
permission bugs) still failed its health check: `curl` got a `500 Internal Server Error`.
`journalctl` on the Pi showed the real cause:
```
System.NotSupportedException: JsonTypeInfo metadata for type '<>f__AnonymousType0`1[System.String]'
was not provided by TypeInfoResolver of type '[Microsoft.AspNetCore.OpenApi.OpenApiJsonSchemaContext]'.
```
`/health` was implemented as `Results.Ok(new { status = "ok" })` — an anonymous type serialized
via `System.Text.Json`'s default reflection-based resolver. Native AOT publishes don't support
reflection-based JSON serialization (there's no metadata to reflect over at runtime after
trimming), so any type serialized this way needs an explicit source-generated
`JsonSerializerContext` instead. This bug was never caught locally because local testing only
ever exercised `dotnet build`/`dotnet run` (JIT, reflection available) or checked AOT-published
file listings — never actually ran an AOT-published binary and hit the endpoint.

## Decision

Define response types as concrete records (not anonymous types), and register a
`JsonSerializerContext` via `ConfigureHttpJsonOptions`:
```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
...
internal record HealthResponse(string Status);

[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
```
Verified by actually running the AOT-published binary locally and curling the endpoint (not
just checking that `dotnet publish` succeeds) — `200 OK` with `{"status":"ok"}`.

## Alternatives considered

Disabling `PublishAot` — rejected outright; it's the deployment story the whole project is
built around (see the scope doc and ADR 0001). Falling back to reflection-based JSON via a
compatibility shim — .NET does offer `PublishTrimmed=false`/non-AOT JSON fallbacks, but that
defeats the point of choosing AOT for a lean, fast-starting Pi binary in the first place.

## Consequences

Every future endpoint that returns a new response shape needs its type added to
`AppJsonSerializerContext` (a `[JsonSerializable(typeof(...))]` attribute per type, or a shared
context class covering all of them). Forgetting this produces the same `NotSupportedException`
at request time, not at build or publish time — there's no compile-time guard against it today.
The only way this was actually caught was by running the real AOT binary and hitting the
endpoint; `dotnet build` and even `dotnet publish` both succeed regardless. Any future
correctness verification for AOT-specific behavior should follow the same pattern: build isn't
enough, actually run the published binary.
