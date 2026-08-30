# Architecture Decision Records

Non-obvious structural and technical decisions on this project, with the alternatives that
were considered and rejected. New records follow [`template.md`](template.md).

| # | Decision |
| --- | --- |
| [0001](0001-minimal-apis-over-controllers.md) | Minimal APIs over MVC Controllers |
| [0002](0002-tailscale-oauth-shared-deploy-user.md) | Tailscale OAuth (tag-scoped) and a shared Pi deploy user |
| [0003](0003-numeric-tailscale-ip-in-ci.md) | Use the Pi's numeric Tailscale IP, not MagicDNS, in the deploy workflow |
| [0004](0004-no-tag-ci-acl-restriction-yet.md) | Defer restricting `tag:ci`'s Tailscale ACL grant |
| [0005](0005-objcopy-cross-toolchain-fix.md) | Point Native AOT's objcopy at the arm64 cross-toolchain explicitly |
| [0006](0006-drop-invariantglobalization-ci-check.md) | Drop the CI check for InvariantGlobalization in the AOT publish |
| [0007](0007-source-generated-json-for-aot-endpoints.md) | Use source-generated JSON serialization for minimal API responses |
| [0008](0008-agent-framework-aot-compatibility.md) | Stay on Native AOT for the agentic Precise Mode rewrite |
| [0009](0009-builtin-logging-over-serilog.md) | Built-in `ILogger` + JSON console formatter over Serilog |
| [0010](0010-opentelemetry-tracing-and-metrics.md) | OpenTelemetry tracing and metrics, verified against Native AOT |
| [0011](0011-hosted-otel-backend-static-collector-config.md) | Hosted Grafana Cloud backend, static OTel Collector config on the Pi |
| [0012](0012-builtin-rate-limiting-per-process-hashed-key.md) | Built-in ASP.NET Core rate limiting, in-memory, partitioned by hashed API key |
| [0013](0013-async-job-based-generation-invocation.md) | Async job-based invocation model for the generation pipeline |
| [0014](0014-local-filesystem-track-storage.md) | Local filesystem storage for generated tracks, keyed by trackId |
| [0015](0015-in-memory-vector-retrieval-for-rag-kb.md) | Self-written in-memory vector retrieval over a vector DB for the RAG knowledge base |
| [0016](0016-aot-safe-json-typeinfo-overload.md) | Always serialize via `JsonTypeInfo<T>`, never `JsonSerializerOptions` with a resolver |
| [0017](0017-patch-based-track-repair.md) | Patch-based track repair over full-track regeneration |
| [0018](0018-xunit-v3-and-nsubstitute.md) | xUnit v3 on Microsoft.Testing.Platform, and NSubstitute over Moq |
| [0019](0019-xcodegen-over-committed-xcodeproj.md) | XcodeGen-generated project over a committed .xcodeproj |
