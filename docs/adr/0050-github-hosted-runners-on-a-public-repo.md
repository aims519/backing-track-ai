# 0050. GitHub-hosted runners (arm64 for the API) on the public repo, replacing the self-hosted Mac

Status: Accepted

## Context

CI and deploy moved to a self-hosted runner on the owner's Mac in September 2026, because
GitHub-hosted macOS minutes bill at a 10x multiplier on a private repo and `ios-ci.yml` alone
was exhausting the monthly allowance. The API jobs then ran in `mcr.microsoft.com/dotnet/sdk:10.0`
containers driven by hand with `docker exec` (the job-level `container:` key doesn't work on a
macOS runner), and the deploy job ran on the Mac, which is a permanent tailnet node.

The repo is about to go public. Standard GitHub-hosted runners, macOS and the arm64 Linux ones
included, are free on public repos, so the billing reason for self-hosting is gone. A new reason
against it appears: a self-hosted runner on a public repo is a standing risk, because a workflow
that runs on `pull_request` from a fork executes arbitrary code on the owner's machine. That
machine also holds an unlocked login keychain for signing
([ADR 0040](0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md)) and sits on the
tailnet with a route to the Pi.

## Decision

Run every workflow on GitHub-hosted runners.

- **`verify.yml` and the deploy `publish` job: `ubuntu-24.04-arm`**, with `actions/setup-dotnet`
  and no container. linux-arm64 is then a native build for the Pi, so the cross toolchain and the
  `ObjCopyName` override from [ADR 0005](0005-objcopy-cross-toolchain-fix.md) stay unnecessary,
  and the AOT check in `verify.yml` builds for the same architecture it ships.
- **Deploy job: `ubuntu-24.04`**, with the `tailscale/github-action` step restored, since the
  runner is ephemeral and no longer a tailnet node ([ADR 0002](0002-tailscale-oauth-shared-deploy-user.md)).
  The `TS_OAUTH_*` secrets were never removed.
- **iOS jobs: `macos-26`**, with `DEVELOPER_DIR` pinned to Xcode 26.6 so the image's default
  Xcode can't change under the workflow. The test destination changes from iPhone 16 to
  iPhone 17 (both iOS 26.5), because the image has no iPhone 16 simulator.

## Alternatives considered

**Keep the self-hosted runner.** Rejected for the fork-PR exposure above. GitHub's own guidance
is not to use self-hosted runners with public repos. Mitigations exist (require approval for
all outside contributors, ephemeral just-in-time runners), but they add upkeep to protect a
machine whose only job here is to save minutes that are now free.

**`ubuntu-24.04` (x64) with the cross toolchain restored.** This is what the pipeline used
before the self-hosted move. Rejected: the cross toolchain and `ObjCopyName` workaround come
back, the AOT check builds for linux-x64 rather than the Pi's architecture, and the arm64
runner removes both costs. arm64 hosted runners run on a private repo too (verified on the
first run of the PR that introduced this), so nothing here depends on the repo being public yet.

**Containers on hosted Linux runners** (`container: mcr.microsoft.com/dotnet/sdk:10.0`). Rejected:
nothing about the job needs a container once it isn't running on macOS, and `setup-dotnet`
is what the workflow used before.

## Consequences

- While the repo is private, `macos-26` jobs bill at 10x, so merging before the flip spends
  minutes on every iOS run. The `ubuntu-24.04-arm` jobs run fine on a private repo.
- The release signing path changes with no prior run on a clean machine. The archive step used
  to sign with a development identity from the owner's login keychain; a hosted runner has none,
  so `-allowProvisioningUpdates` with the Admin API key has to create a cloud-managed one. The
  first push to `main` touching `app/` is the real test. If it fails, no TestFlight build
  appears until it is fixed. This removes ADR 0040's login-keychain and `SessionCreate`
  consequences and its "only works while the Mac is on" limit.
- Each deploy now pays for Tailscale connection and a cold .NET install, adding up to a minute
  or two over the warm local runner.
- Image changes are GitHub's schedule, not ours. Xcode is pinned, but the simulator list is
  not: if iPhone 17 / iOS 26.5 leaves the image, `ios-ci.yml` fails with a destination-not-found
  error until the destination is updated.
- Fork PRs run on hosted runners with no access to secrets. `deploy.yml` and `ios-release.yml`
  run only on pushes to `main`.
- The runner registered on the Mac and its LaunchAgent should be removed once the first hosted
  runs pass. Until then it can still pick up `[self-hosted, macOS]` jobs, but no workflow
  requests that label any more.
