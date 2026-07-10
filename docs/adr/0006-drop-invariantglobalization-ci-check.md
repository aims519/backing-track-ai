# 0006. Drop the CI check for InvariantGlobalization in the AOT publish

Status: Accepted

## Context

`deploy.yml` originally included a step verifying `Directory.Build.props`'s
`InvariantGlobalization=true` setting actually made it into the AOT-published output, by
grepping the published `BackingTrack.Api.runtimeconfig.json` for
`"System.Globalization.Invariant": true`. The second real CI run (after fixing ADR 0005's
`objcopy` issue) failed on this step: `grep: ./publish/BackingTrack.Api.runtimeconfig.json:
No such file or directory`.

Native AOT publish output doesn't include a `runtimeconfig.json` at all — a local test publish
(`dotnet publish -r osx-arm64 -p:PublishAot=true`) confirmed no such file exists anywhere in
the publish output, and only a `.genruntimeconfig.cache` marker (not the JSON content) exists
in the intermediate `obj` directory. The premise behind the original check was wrong: AOT bakes
runtime configuration directly into the native binary as compile-time feature switches, rather
than reading it from an external JSON file at startup, so there's no config file left to grep
after the fact.

## Decision

Remove the verification step entirely rather than replace it with a different check.

## Alternatives considered

Inspecting the compiled binary directly (e.g. `strings` for globalization-related symbols, or
checking dynamic library dependencies for the absence of an ICU link) — tried locally during
this investigation and rejected. Symbol strings referencing globalization types appear in the
binary regardless of the setting (they're just metadata/reflection artifacts, not evidence of
the feature switch's value), and dynamic library linkage isn't a reliable signal either — a
local macOS test publish still linked against the system's `libicucore` irrespective of the
setting, since Apple platforms have their own globalization code paths. Neither approach
reliably distinguishes "invariant mode is on" from "it's not," and there's no cheap way to
actually execute the cross-compiled arm64 binary from the x86 GitHub runner to test its
behavior directly.

## Consequences

There's no automated CI guard confirming this setting took effect; it's back to trusting the
`Directory.Build.props`-level setting and the fact that `dotnet build` is warning-free. If a
future globalization-related runtime failure shows up on the Pi, the fix is to check behavior
against the actual deployed binary directly (e.g. does an unexpected culture-sensitive
operation throw or behave unexpectedly) rather than trying to statically verify the setting
from CI.
