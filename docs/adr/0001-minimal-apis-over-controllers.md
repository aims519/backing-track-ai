# 0001. Minimal APIs over MVC Controllers

Status: Accepted

## Context

The scope doc commits to publishing the API as Native AOT, for a lean, fast-starting binary
suited to the Raspberry Pi's limited resources. While scaffolding the `BackingTrack.Api`
project, the question of Controllers vs. minimal APIs came up on familiarity grounds
(Controllers is the more commonly seen ASP.NET Core style).

## Decision

Use minimal APIs (`app.MapGet`, `app.MapPost`, etc.), organized under an `Endpoints/` folder.

## Alternatives considered

MVC Controllers (`[ApiController]` / `ControllerBase`) — rejected. MVC's controller discovery
relies on reflection in a way Native AOT's static analysis can't fully see through; attempting
`AddControllers()` on this project produces an explicit build warning (IL2026) that MVC does not
currently support trimming or Native AOT. Choosing Controllers would mean silently breaking the
AOT publish goal, discovered only when a `dotnet publish` either fails or produces a broken
binary — not at `dotnet build` time.

## Consequences

Endpoint definitions are less familiar than Controllers to someone used to traditional
ASP.NET Core MVC, and larger endpoint counts will need deliberate organization (e.g. extension
methods grouping related endpoints) rather than falling out of one class per resource for free.
In exchange, the project keeps the Native AOT publish path the scope doc commits to.
