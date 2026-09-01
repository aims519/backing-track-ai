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
    Features/                 SwiftUI screens, one subfolder per feature (Generate/, Settings/)
    Models/                   Domain models — the request/parameter types, mirroring api/src/BackingTrack.Core
    Networking/               API client talking to the Pi-hosted backend
    Resources/                Assets.xcassets (app icon, accent color)
  BackingTrackTests/          Unit tests (Swift Testing framework)
```

`GenerateTrackView` is the root screen: the chord/key/tempo/style/time-signature input form for
Precise mode. It builds a `GenerationRequest` but doesn't send it yet — the API client and the
generating → ready/failed flow are still to come, so `Generate` currently just previews the
request it would POST. Vibe mode is shown in the selector but disabled (its backend doesn't
exist yet). See [ADR 0020](../docs/adr/0020-structured-chord-picker-over-free-text.md) for why
chord entry is a constrained picker rather than a text field.

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
