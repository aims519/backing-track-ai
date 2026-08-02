# 0012. Built-in ASP.NET Core rate limiting, in-memory, partitioned by hashed API key

Status: Accepted

## Context

The `/tracks` endpoint group needed protection against a client (or a misbehaving script)
hammering it with requests, on top of the existing `ApiKeyFilter` auth. `PublishAot=true` is
already load-bearing (ADR 0007, ADR 0008), so any package added has to be AOT-compatible. The
API currently runs as a single process on the Pi (ADR 0011), not behind a load balancer with
multiple instances.

## Decision

Use the framework's built-in `Microsoft.AspNetCore.RateLimiting` middleware
(`System.Threading.RateLimiting` + `AddRateLimiter`/`UseRateLimiter`), which ships in the shared
framework with no extra NuGet dependency — consistent with the built-in-over-third-party
preference already set in ADR 0009.

The policy is a `FixedWindowRateLimiter`, held in-process (no distributed backing store),
configured via a `RateLimitSettings` (`PermitLimit`, `WindowSeconds`) options class bound from
`appsettings.*.json`, following the same pattern as `AuthSettings`/`OtelSettings`.

Requests are partitioned by a SHA-256 hash of the `X-API-Key` header (truncated to 8 hex chars),
not by client IP and not by the raw key value. Rejected requests get a `429`, a `Retry-After`
header, and a warning log tagged with the hashed partition key rather than the raw key.

## Alternatives considered

A third-party package (e.g. `AspNetCoreRateLimit`) — rejected. It adds a dependency with no
guarantee of AOT/trim annotation, for functionality the framework now ships natively.

A distributed store (e.g. Redis-backed limiter) — rejected as unnecessary. The API runs as a
single process on the Pi; a distributed counter would add operational complexity for a
constraint that doesn't exist yet.

Partitioning by client IP — rejected. The API already has a per-client identity concept
(`X-API-Key`), and Tailscale-routed traffic (ADR 0002) can make IP a poor proxy for "which
client" compared to the key itself.

Logging or partitioning by the raw API key value — rejected. The key is a secret validated via
constant-time comparison in `ApiKeyFilter`; writing it into log lines (which flow to Grafana
Cloud via OTel, ADR 0010/0011) or using it verbatim as an in-memory partition label would leak
it into a channel wider than intended. A hash gives the same per-client correlation without
exposing the value.

## Consequences

Rate limit state resets on process restart and is per-instance — correct for the current
single-process Pi deployment, but if the API is ever scaled horizontally or made to survive
restarts without a brief reset window, this limiter needs to move to a shared/distributed store
(e.g. Redis), which is a real rework, not a config change.

The hashed partition key is one-way: it's enough to see "this client got rate-limited
repeatedly" in logs, but not enough to look up which client that was without a separate
hash-to-client mapping. That's fine while there's a single shared `Auth:ApiKey`; it becomes a
real gap if/when multiple named client keys are introduced, at which point client identity
should probably be tracked explicitly rather than reverse-engineered from hashes.
