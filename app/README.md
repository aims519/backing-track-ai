# BackingTrack (iOS)

Native SwiftUI app. See [`/docs/backing-track-generator-scope.md`](../docs/backing-track-generator-scope.md)
for the full project scope, and [`/docs/adr/`](../docs/adr/) for the reasoning behind non-obvious
structural decisions.

## Structure

```text
app/
  project.yml                 XcodeGen project spec — the .xcodeproj is generated from this, not committed
  BackingTrack/
    App/                      App entry point (BackingTrackApp.swift)
    Features/                 SwiftUI screens, one subfolder per feature (Generate/, Progress/, Settings/)
    Components/               Shared view building blocks (SectionCard)
    Models/                   Domain models — the request/parameter types, mirroring api/src/BackingTrack.Core
    Networking/               API client talking to the Pi-hosted backend
    Persistence/              Local SwiftData store for job/track history
    Resources/                Assets.xcassets (app icon, accent color)
  BackingTrackTests/          Unit tests (Swift Testing framework)
```

`GenerateTrackView` is the root screen: the chord/key/tempo/style/time-signature input form for
Precise mode. `Generate` submits the `GenerationRequest` to `POST /tracks/generate` via
`BackingTrackAPIClient` (`Networking/`). On acceptance, `GenerationProgressView` (`Features/Progress/`)
takes over as a full-screen cover, polling `GET /tracks/status/{jobId}` until the job reaches a
terminal state and showing a "Track ready" or failure result. Vibe mode is shown in the selector
but disabled (its backend doesn't exist yet). See
[ADR 0020](../docs/adr/0020-structured-chord-picker-over-free-text.md) for why chord entry is a
constrained picker rather than a text field,
[ADR 0022](../docs/adr/0022-plaintext-http-over-tailscale-ats-disabled.md) for the plain-HTTP /
ATS posture, and
[ADR 0023](../docs/adr/0023-client-derived-generation-progress.md) for how the progress screen
derives its feedback and handles a stuck job, and
[ADR 0024](../docs/adr/0024-client-side-swiftdata-job-history.md) for the local SwiftData store
(`Persistence/`) that mirrors every job as it polls.

## Setup

Requires Xcode 26+ and [XcodeGen](https://github.com/yonaskolb/XcodeGen):

```bash
brew install xcodegen
```

Signing needs one local, machine-specific file first (gitignored — a team ID is tied to your
Apple ID, not the project):

```bash
cp Config/Local.xcconfig.example Config/Local.xcconfig
# then edit Config/Local.xcconfig and fill in DEVELOPMENT_TEAM — see the comments in
# Config/Local.xcconfig.example for how to find it
```

Generate the Xcode project, then open it:

```bash
cd app
xcodegen generate
open BackingTrack.xcodeproj
```

### API base URL

The app reads the API's base URL from the `API_BASE_URL` build setting (surfaced as the
`APIBaseURL` Info.plist key, read by `APIConfiguration`). **Debug** builds default to
`http://localhost:5198` — a local `dotnet run` of the API (see [`api/README.md`](../api/README.md))
— so the simulator works with no extra setup. For **Release** / on-device builds, set the Pi's
Tailscale URL in `Config/Local.xcconfig` (gitignored, per-machine like `DEVELOPMENT_TEAM`);
`Config/Local.xcconfig.example` shows the line and the `//`-escaping the xcconfig format needs.
A Release build with no value set fails fast at launch. The transport is plain HTTP over the
Tailscale tunnel — see [ADR 0022](../docs/adr/0022-plaintext-http-over-tailscale-ats-disabled.md).

Paste a matching `Auth:ApiKey` value into the app's Settings screen; it's stored in the
Keychain and sent as `X-API-Key`.

### Status polling

The progress screen polls `GET /tracks/status/{jobId}` every `STATUS_POLL_INTERVAL` seconds
(surfaced as the `StatusPollInterval` Info.plist key, read by `PollingConfiguration`). Debug
builds poll every 1s; the `base` default (Release) is 2s. Unlike `API_BASE_URL`, a missing or
malformed value isn't fatal — it falls back to 2s. The overall poll timeout and consecutive-error
budget are constants on `PollingConfiguration`, not build settings.

Re-run `xcodegen generate` whenever `project.yml` changes, or after adding/removing/moving
source files (Xcode's file list is derived from what's on disk, not tracked incrementally in
`project.yml`).

The generated `.xcodeproj` is gitignored — see [ADR 0019](../docs/adr/0019-xcodegen-over-committed-xcodeproj.md)
for why.

### Signing

Both targets use automatic signing (`CODE_SIGN_STYLE: Automatic` in `project.yml`) with the team
from `Config/Local.xcconfig` — Xcode picks and renews a certificate itself rather than us managing
a provisioning profile by hand. This works with a free "personal team" (no paid Apple Developer
Program membership needed), which is all this project uses since there's no App Store
distribution — see `docs/backing-track-generator-scope.md`. The tradeoff: a personal-team signature
is only valid for 7 days, so a build installed on-device needs reinstalling from Xcode
periodically rather than running indefinitely.

## Building & testing from the command line

```bash
xcodebuild -project BackingTrack.xcodeproj -target BackingTrack -sdk iphonesimulator \
  CODE_SIGNING_ALLOWED=NO build

xcodebuild -project BackingTrack.xcodeproj -scheme BackingTrack \
  -destination 'platform=iOS Simulator,name=iPhone 16' test
```

Note `test` deliberately doesn't carry `CODE_SIGNING_ALLOWED=NO` — an unsigned binary has no
app-identifier entitlement, which breaks Keychain access (`APIKeyStoreTests`) at run time even
though it builds fine. Relies on Xcode's normal automatic local signing instead; `ios-ci.yml`
uses the ad-hoc `CODE_SIGN_IDENTITY="-"` identity for the same reason, since CI has no signing
account to automatically sign with.

The `test` destination needs a matching iOS Simulator runtime installed for your Xcode version
(Xcode > Settings > Components) — `xcrun simctl list devices available` shows what's installed,
and `xcrun simctl list runtimes` shows what's downloaded. Swap `iPhone 16` for a device installed
locally.

## CI

[`.github/workflows/ios-ci.yml`](../.github/workflows/ios-ci.yml) runs the same build + test on a
macOS runner for PRs/pushes touching `app/`. Kept as its own workflow, separate from
[`api-ci.yml`](../.github/workflows/api-ci.yml) (the `api/` pipeline), since macOS runners bill at a 10x
minute multiplier on this (private) repo — no reason to pay that on `api/`-only changes.
