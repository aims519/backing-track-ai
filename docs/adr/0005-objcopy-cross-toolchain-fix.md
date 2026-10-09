# 0005. Point Native AOT's objcopy at the arm64 cross-toolchain explicitly

Status: Accepted. No longer needed since the publish job builds natively on arm64 ([0050](0050-github-hosted-runners-on-a-public-repo.md))

## Context

The first real run of `.github/workflows/deploy.yml` failed during `dotnet publish
-r linux-arm64 -p:PublishAot=true`:
```
objcopy: Unable to recognise the format of the input file
`bin/Release/net10.0/linux-arm64/native/BackingTrack.Api'
error MSB3073: The command ""objcopy" --only-keep-debug ..." exited with code 1.
```
Native AOT's build targets run `objcopy --only-keep-debug` as a post-build step to split debug
symbols out of the compiled binary. On an `ubuntu-24.04` runner cross-compiling for
`linux-arm64`, the plain `objcopy` on `PATH` is the host's own (x86_64) build of binutils —
it can't parse an arm64 ELF binary's format, hence the failure. `gcc-aarch64-linux-gnu` /
`binutils-aarch64-linux-gnu` (already installed for the cross-compile itself) provide a
separate, correctly-targeted `aarch64-linux-gnu-objcopy` binary, but the .NET SDK doesn't pick
it automatically just because the packages are installed.

## Decision

Pass `-p:ObjCopyName=aarch64-linux-gnu-objcopy` to `dotnet publish`, an MSBuild property the
Native AOT build targets read to decide which `objcopy` binary to invoke for the debug-symbol
split step.

## Alternatives considered

`-p:StripSymbols=false` — disables the debug-symbol split entirely, sidestepping the need for
a correctly-targeted `objcopy` at all. Rejected: it keeps debug symbols embedded in the shipped
binary (slightly larger, and works against the "lean" rationale for choosing Native AOT in the
first place), to avoid a one-line fix that keeps stripping working correctly.

## Consequences

If the arm64 cross-toolchain's `objcopy` binary name ever changes (a different Ubuntu runner
image versioning it differently, or switching to `clang`'s `llvm-objcopy` instead of GNU
binutils), this property needs updating alongside it. This is also why `deploy.yml` pins
`runs-on: ubuntu-24.04` rather than floating `ubuntu-latest` — a GitHub-side runner image bump
could silently change the installed toolchain's binary naming and break this fix without any
change on this repo's side.
