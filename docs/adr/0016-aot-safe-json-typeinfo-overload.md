# 0016. Always serialize via JsonTypeInfo<T>, never JsonSerializerOptions with a resolver

Status: Accepted

## Context

`PublishAot=true` is already load-bearing (ADR 0007, ADR 0008), and several call sites across
the generation pipeline embedded a `CandidateTrack`/`TrackRepairPatch`/failure list into a log
message or an LLM prompt via:

```csharp
JsonSerializer.Serialize(value, new JsonSerializerOptions { TypeInfoResolver = SomeJsonContext.Default })
```

This compiles cleanly and passes `dotnet build`/`dotnet test` without a single warning, because
Native AOT's trimming/AOT analyzer only runs during `dotnet publish -p:PublishAot=true` — the
actual deploy step, not local dev or CI's build/test stage. After enabling
`TreatWarningsAsErrors` project-wide, the next push to `main` broke the Pi deploy: six call sites
across `RepairStep.cs`, `GenerateStep.cs`, `GenerationJobWorkerHostedService.cs`, and
`ValidateCandidateTrackExecutor.cs` all failed with `IL2026`/`IL3050`
(`RequiresDynamicCode`/`RequiresUnreferencedCode`) turned into build errors. The generic
`Serialize<TValue>(TValue, JsonSerializerOptions)` overload is annotated as AOT-unsafe
unconditionally — the analyzer can't prove at the call site that the `TypeInfoResolver` supplied
at runtime is actually the source-generated one, even when it obviously is.

## Decision

Serialize via the AOT-safe overload that takes a `JsonTypeInfo<T>` directly, using the property
already exposed on the relevant source-generated `JsonSerializerContext`:

```csharp
JsonSerializer.Serialize(value, SomeJsonContext.Default.SomeType)
```

Applied to all six sites: `CandidateTrackJsonContext.Default.CandidateTrack` and
`TrackRepairJsonContext.Default.{TrackRepairPatch, IReadOnlyListValidationFailure}`.

One of the six (`GenerateStep`'s regeneration prompt) had also set `WriteIndented = true`. The
`JsonTypeInfo<T>` overload takes no per-call formatting options, so that call site now emits
compact JSON instead of pretty-printed. Accepted as a cosmetic-only change — it's whitespace in
an LLM prompt, not content the model needs, and it now matches every other call site's compact
formatting rather than being the one inconsistent exception.

Verified for real, not just against a clean local build: ran the actual
`dotnet publish api/src/BackingTrack.Api -c Release -r linux-arm64 -p:PublishAot=true
--self-contained` locally. It passed through IL trimming/AOT analysis with zero `IL2026`/`IL3050`
warnings and only failed afterward on a missing linux-arm64 cross-linker (`clang: invalid linker
name '-fuse-ld=bfd'`) — a macOS tooling gap unrelated to the fix, the same cross toolchain CI's
"Install arm64 cross toolchain" step provides. Confirmed green on the actual CI run afterward.

## Alternatives considered

- **Suppress `IL2026`/`IL3050` for these call sites** (`#pragma warning disable` or
  `[UnconditionalSuppressMessage]`) rather than fixing them. Rejected: the whole point of
  `TreatWarningsAsErrors` was to make analyzer violations impossible to miss; suppressing the
  exact category of warning this project depends on defeats it, and reintroduces the silent
  local-build-passes-CI-fails gap this ADR exists to close.
- **Keep `WriteIndented = true` by round-tripping through `JsonNode`** (serialize compact via the
  AOT-safe overload, then `JsonNode.Parse(...).ToJsonString(new JsonSerializerOptions
  { WriteIndented = true })`, since `JsonNode`'s own serialization isn't reflection-based).
  Rejected as unnecessary complexity for a purely cosmetic difference in one prompt's embedded
  JSON.

## Consequences

- Every future `JsonSerializer.Serialize`/`Deserialize` call against a type covered by an
  existing `JsonSerializerContext` must use the `JsonTypeInfo<T>` overload, not
  `new JsonSerializerOptions { TypeInfoResolver = ... }` — a normal `dotnet build`/`dotnet test`
  run will not catch a violation; only `dotnet publish -p:PublishAot=true` will, and only because
  `TreatWarningsAsErrors` now makes that analysis fatal instead of an ignorable warning stream.
  This is worth remembering for anyone (human or agent) adding new debug/prompt-formatting log
  lines in this codebase.
- A type not yet added to a `[JsonSerializable]`-decorated context has no `JsonTypeInfo<T>`
  property to reach for — the fix pattern requires the type already being source-gen-registered,
  which in practice it will be (every domain type serialized here already flows through
  `CandidateTrackJsonContext` or `TrackRepairJsonContext` for production persistence/repair, not
  just these debug/prompt call sites).
