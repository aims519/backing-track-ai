# 0008. Stay on Native AOT for the agentic Precise Mode rewrite

Status: Accepted

## Context

The scope doc was updated to rebuild Precise Mode as an agentic, RAG-grounded workflow using
Microsoft Agent Framework, rather than a single LLM call. This raised a real question before
any of it gets built: does Native AOT — a decision already load-bearing across ADR 0001
(minimal APIs over Controllers) and ADR 0007 (source-generated JSON) — still hold once a much
larger, less-charted dependency (an agent framework, RAG retrieval, a vector store, tool
calling) sits in front of it?

Investigated directly against the `microsoft/agent-framework` GitHub repo rather than relying
on blog posts or marketing claims:

- `dotnet/Directory.Build.props` sets `IsAotCompatible=true` repo-wide for every project
  targeting net7.0+ — not opt-in per package. This is the property that makes the SDK emit
  trim/AOT analyzer warnings during the library's own build, so it's the maintainers holding
  the whole framework to that bar, not an unverified claim.
- `Microsoft.Agents.AI.Anthropic` (the client this project would actually use) inherits this
  with no override, same as `Microsoft.Agents.AI.OpenAI` and the core packages.
- A real AOT bug did surface and get fixed:
  [#6744](https://github.com/microsoft/agent-framework/issues/6744) —
  `CheckpointManager.CreateJson` for declarative workflows relied on a reflection fallback that
  silently worked under JIT but broke under AOT; the framework team shipped an AOT-safe
  `JsonSerializerContext` fix. Evidence they test AOT scenarios and fix what breaks, not just a
  green checkbox.
- The one clearly "doesn't support NativeAOT" report found
  ([#5969](https://github.com/microsoft/agent-framework/issues/5969)) turned out to be a
  third-party LLM client (Ollama, via OllamaSharp) not opting its own types into AOT — closed
  by the maintainer as "not an Agent Framework issue," with the framework's own custom-tool
  pattern (source-generated `JsonSerializerContext`, the exact pattern from ADR 0007) confirmed
  working.

Caveat: `IsAotCompatible=true` is a static-analysis commitment, not a runtime guarantee against
every possible AOT surprise — the checkpointing bug above is proof the analyzer can miss things
that only surface at actual runtime under trimming.

## Decision

Stay on Native AOT rather than reconsidering it now that Precise Mode is getting more complex.

The case for reconsidering was real: AOT's main practical benefit — fast cold start — barely
matters for a persistent, single-user systemd service that starts once and stays up (noted
during the original deployment planning too), and every new dependency added from here
(RAG retrieval, a vector store, tool-calling types) needs its own AOT-safety check rather than
inheriting one from Agent Framework automatically. That's a real, recurring cost.

But dropping AOT now isn't free either: the deploy pipeline (`.github/workflows/deploy.yml`,
the systemd unit in `scripts/pi-bootstrap.sh`) assumes a self-contained single binary with no
.NET runtime installed on the Pi — reversing that means provisioning and maintaining a runtime
on the device instead, undoing real, already-working infrastructure for a benefit (faster
startup) that doesn't matter for this workload. And the friction hit so far (`objcopy`, the
`/health` JSON serialization bug, directory permissions) was all one-time and solvable, not a
sign of a dead end.

## Alternatives considered

Drop `PublishAot` for the API, keep a framework-dependent or self-contained (non-AOT) deploy —
rejected. Would remove a chunk of the "lean, edge-deployed on constrained hardware" portfolio
differentiator this project is explicitly built around (see the scope doc's Differentiation
section), require re-provisioning the Pi to host a .NET runtime, and doesn't actually eliminate
the underlying risk — a RAG/vector-store dependency can just as easily have its own
Pi-arm64-specific problems unrelated to AOT.

## Consequences

Going forward, AOT compatibility becomes a **hard filter, not an afterthought**, when choosing
the RAG/vector-store library for Precise Mode's retrieval step — the same diligence just
applied to Agent Framework (check the actual repo for `IsAotCompatible`/trim annotations,
search its issue tracker for AOT reports, don't just trust a README) needs to happen *before*
committing to a library, not discovered mid-implementation the way the `/health` JSON bug was
discovered only by actually running the published binary. Any new tool/function type added for
agent tool-calling needs its own `JsonSerializerContext` registration, following the pattern
established in ADR 0007 — forgetting this produces the same class of runtime-only
`NotSupportedException` that `/health` hit, not a build-time error.
