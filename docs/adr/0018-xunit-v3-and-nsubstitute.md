# 0018. xUnit v3 on Microsoft.Testing.Platform, and NSubstitute over Moq

Status: Accepted

## Context

The test stack was xUnit v2.9.3 + Moq 4.20.72, unchanged since the project
started. Two things forced a look:

- **Runner.** xUnit v2 runs on the legacy VSTest host. On the .NET 10 SDK,
  `dotnet test` against the VSTest target now fails outright for
  Microsoft.Testing.Platform (MTP)-capable frameworks:
  *"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
  on .NET 10 SDK and later."* This repo targets `net10.0`, publishes Native AOT
  (see [0008](0008-agent-framework-aot-compatibility.md),
  [0010](0010-opentelemetry-tracing-and-metrics.md)), and MTP is both the
  trimming/AOT-aligned test host and the direction MSTest, NUnit, and xUnit have
  all converged on.
- **Mocking library.** Moq's most recent release, 4.20.72, is from September 2024
  — roughly two years stale. Its 4.20.0 release (August 2023) shipped a
  closed-source `SponsorLink` dependency that read the developer's git-config
  email at build time and sent a hash to a remote endpoint; it was removed in
  4.20.2 after community backlash, but both the stalled cadence and the trust
  question remain.

CI ([`.github/workflows/verify.yml`](../../.github/workflows/verify.yml)) runs
build + test on every PR and before every deploy, so any runner change has to
work headless.

## Decision

- Move the test projects to **xUnit v3 on Microsoft.Testing.Platform**. Each test
  project builds as an executable and runs via the MTP protocol.
- Replace **Moq with NSubstitute** across both test projects (7 files).

Both changes ship together on the `feature/unit-test-and-mocking-upgrades` branch.

## Alternatives considered

- **Stay on xUnit v2.** Rejected — `dotnet test` no longer runs v2's VSTest
  target on the .NET 10 SDK, and v2 is maintenance-only.
- **xUnit v3 but keep the VSTest bridge.** Possible short-term, but it
  reintroduces the reflection-heavy host that the AOT/trimming work is trying to
  avoid, and the bridge is on the same deprecation path.
- **Keep Moq at its current version.** SponsorLink is already gone, so this is
  defensible on security grounds today — but the ~2-year release gap means
  framework-compat and bug fixes aren't landing, and the call-site syntax
  (`.Object`, `It.IsAny<>()`, `Verify(..., Times.Once)`) is noisier than the
  alternatives. No upside to staying.
- **FakeItEasy instead of NSubstitute.** Equivalent capability. NSubstitute was
  chosen for the terser call sites (`sub.M(Arg.Any<T>()).Returns(v)`,
  `sub.Received(1).M(...)`) and the larger current community footprint.

## Consequences

- **CI has to change.** `verify.yml`'s test step and its TRX / coverage flags are
  built around VSTest-style `--collect` / `--logger`; MTP takes different
  arguments. The pipeline from
  [0016](0016-aot-safe-json-typeinfo-overload.md)-era work needs reworking before
  this branch's CI goes green.
- **Test projects are now independently runnable** (`dotnet run --project
  <test-project>`), which is a small win for local debugging.
- **No strict-mock enforcement.** NSubstitute has no equivalent of Moq's
  `MockBehavior.Strict`. The suite never used strict mode, so nothing is lost,
  but it's a behavioural difference to be aware of when adding tests.
- **One-time churn.** 7 test files rewritten; assertion style is now
  NSubstitute-idiomatic (`Received(1)` rather than `Verify(..., Times.Once)`) and
  won't match older xUnit-v2 / Moq examples elsewhere.
- **Contributor ramp.** Muscle memory from Moq and xUnit v2 won't transfer
  cleanly; both libraries changed at once.
