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
| [0020](0020-structured-chord-picker-over-free-text.md) | Structured chord picker over free-text entry on the generation input screen |
| [0021](0021-code-drawn-visual-assets.md) | Code-drawn visual assets (app icon, launch screen), no image-model output |
| [0022](0022-plaintext-http-over-tailscale-ats-disabled.md) | Plaintext HTTP to the API over Tailscale; App Transport Security disabled (superseded by 0042) |
| [0023](0023-client-derived-generation-progress.md) | Client-derived generation progress copy and a client-side poll timeout |
| [0024](0024-client-side-swiftdata-job-history.md) | Client-side SwiftData job/track history, mirrored from the poll loop |
| [0025](0025-client-job-reconciler-over-jobtracker-extraction.md) | A standalone client-side JobReconciler, not the JobTracker extraction |
| [0026](0026-bundled-soundfont-and-idempotent-audio-setup.md) | Bundled SoundFont and idempotent audio-engine setup |
| [0027](0027-generation-eval-harness.md) | End-to-end generation eval harness as an in-process console app |
| [0028](0028-silent-bar-repair-via-whole-part-replacement.md) | SilentBar repair via whole-part replacement, not a surgical bar-fill |
| [0029](0029-critic-tool-call-over-per-criterion-structured-output.md) | Tool-forced single verdict over per-criterion structured output for the Theory Critic |
| [0030](0030-critic-eval-as-a-separate-code-based-project.md) | Critic judge eval as a separate, code-based project |
| [0031](0031-message-threaded-attempt-counters-over-scoped-job-context.md) | Message-threaded attempt counters over a scoped GenerationJobContext |
| [0032](0032-explicit-key-mode-field-for-scale-matching.md) | Explicit `KeyMode` field, song-wide, over inferring mode or per-chord scale matching |
| [0033](0033-critic-assumes-pitch-legality-instead-of-rechecking-it.md) | Critic assumes pitch legality rather than re-checking it |
| [0034](0034-rule-based-fallback-generator-bypasses-critic.md) | Rule-based fallback generator bypasses the critic loop |
| [0035](0035-hand-authored-repair-schema-for-responses-api.md) | Hand-authored JSON schema for the repair step, over auto-generation |
| [0036](0036-per-step-model-tier-split-for-cost.md) | Per-step model and Flex-tier split for LLM cost, over a single model everywhere |
| [0037](0037-streaming-run-for-cancellable-generation-jobs.md) | Streaming run API (`RunStreamingAsync`/`StreamingRun.CancelRunAsync`) for cancellable generation jobs |
| [0038](0038-direct-status-emitter-over-workflow-messaging.md) | Direct status emitter over workflow-graph messaging for progress feedback |
| [0039](0039-apns-push-from-api-on-terminal-job-state.md) | APNs push from the API on terminal job state, over client-side background completion |
| [0040](0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md) | TestFlight upload on every push to main with plain `xcodebuild` and cloud signing, over fastlane / Xcode Cloud / tag-triggered releases |
| [0041](0041-client-side-per-style-instruments-and-effects-chain.md) | Client-side per-style instrument mapping and a fixed per-voice effects chain, over API-chosen instruments |
| [0042](0042-https-via-tailscale-serve-ats-enabled.md) | HTTPS to the API via `tailscale serve`; App Transport Security left on |
| [0043](0043-ttl-eviction-of-finished-jobs-from-the-in-memory-store.md) | TTL eviction of finished jobs from the in-memory job store, over evicting immediately or keeping them forever |
| [0044](0044-metric-and-span-naming-and-tag-conventions.md) | Metric and span naming, hard-coded tag values, and what may be a tag, over ad-hoc names and enum-derived values |
| [0045](0045-home-dashboard-as-navigation-root.md) | A dashboard as the navigation root, owning the form and history view models, over a tab bar or a form-owned model |
| [0046](0046-minver-from-git-tags-for-api-version.md) | Version the API with MinVer from git tags with the commit SHA as build metadata, over SourceRevisionId alone, the CI run number, or a hand-edited version |
| [0047](0047-automatic-rollback-and-release-pruning-on-the-pi.md) | Roll back to the previous release when any post-deploy step fails and prune to the newest five after a good deploy, over manual recovery, blue/green, or age-based or cron pruning |
| [0048](0048-additive-only-api-changes-instead-of-url-versioning.md) | Additive-only API changes with a tolerant client, over URL versioning or a version header |
