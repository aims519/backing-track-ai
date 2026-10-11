# BackingTrack (iOS)

Native SwiftUI app. See [`/docs/backing-track-generator-scope.md`](../docs/backing-track-generator-scope.md)
for the full project scope, and [`/docs/adr/`](../docs/adr/) for the reasoning behind non-obvious
structural decisions.

## Structure

```text
app/
  project.yml                 XcodeGen project spec — the .xcodeproj is generated from this, not committed
  Config/                     Local.xcconfig (gitignored, per-machine) and its .example; ExportOptions.plist for TestFlight
  Tools/                      generate_icon.py — draws the app icon and launch logo (ADR 0021)
  BackingTrack/
    App/                      App entry point, AppDelegate, notification permission and routing
    Features/                 SwiftUI screens, one subfolder per feature
      Home/                     dashboard — the navigation root (ADR 0045)
      Generate/                 chord/key/tempo/style input form and chord picker
      Progress/                 full-screen generation progress and timeline
      History/                  past jobs list and detail
      Player/                   playback screen, chord strip, Now Playing integration
      Settings/                 API key entry
    Audio/                    AVAudioEngine playback: sequence building, instruments, audio session
    Components/               Shared view building blocks (SectionCard)
    Models/                   Request, job status and track types (mostly mirroring the API's DTOs) plus app-side types such as Chord and MusicStyle
    Networking/               API client for the Pi-hosted backend, polling config, API key store
    Persistence/              Local SwiftData store for job/track history
    Resources/                Assets.xcassets (app icon, accent color, launch logo) and the GeneralUser GS SoundFont
  BackingTrackTests/          Unit tests (Swift Testing framework), mirroring the source layout; Support/ holds fakes
```

`HomeView` is the root screen: a dashboard showing the five most recent jobs, any job still in
flight, and a "New Track" button ([ADR 0045](../docs/adr/0045-home-dashboard-as-navigation-root.md)).
It owns the `NavigationStack` and pushes everything else; Settings opens as a sheet.

- **Generate:** `GenerateTrackView` is the chord/key/tempo/style/time-signature form for Precise
  mode. It submits a `GenerationRequest` to `POST /tracks/generate` via `BackingTrackAPIClient`
  (`Networking/`). Chord entry is a constrained picker rather than a text field
  ([ADR 0020](../docs/adr/0020-structured-chord-picker-over-free-text.md)).
- **Progress:** on acceptance, `GenerationProgressView` (`Features/Progress/`) takes over as a
  full-screen cover, polling `GET /tracks/status/{jobId}` until the job reaches a terminal state
  ([ADR 0023](../docs/adr/0023-client-derived-generation-progress.md) covers how it derives its
  feedback and handles a stuck job). If you leave the app first, the API sends a push notification
  when the job finishes ([ADR 0039](../docs/adr/0039-apns-push-from-api-on-terminal-job-state.md)).
- **History:** every job is mirrored into a local SwiftData store (`Persistence/`) as it polls, and
  unfinished jobs are re-checked with the server when you open History or pull to refresh, and the
  dashboard keeps in-flight rows fresh
  ([ADR 0024](../docs/adr/0024-client-side-swiftdata-job-history.md)).
- **Player:** `PlayerView` loads the finished track (the cached copy if there is one, otherwise a
  download) and plays it on-device through `AVAudioEngine` with a bundled SoundFont. Instruments and effects are chosen per style on the
  client ([ADR 0026](../docs/adr/0026-bundled-soundfont-and-idempotent-audio-setup.md),
  [ADR 0041](../docs/adr/0041-client-side-per-style-instruments-and-effects-chain.md)).

Only Precise mode is available today. Vibe mode (generative audio via a third-party service) is
planned and deliberately hidden in the app until its backend exists; the `vibe` case stays in
`GenerationMode` so history rows and the API's `Mode` field can still represent it.

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

`DEVELOPMENT_TEAM` should be a paid-program team if you want to run on a device; see [Signing](#signing).

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
A Release build with no value set fails fast at launch. The transport uses HTTPS (certificate generated via `tailscale serve`) — see [ADR 0042](../docs/adr/0042-https-via-tailscale-serve-ats-enabled.md).

Paste a matching `Auth:ApiKey` value into the app's Settings screen; it's stored in the
Keychain and sent as `X-API-Key`.

### Status polling

The progress screen polls `GET /tracks/status/{jobId}` every `STATUS_POLL_INTERVAL` seconds
(surfaced as the `StatusPollInterval` Info.plist key, read by `PollingConfiguration`). Both Debug
and Release (the `base` setting) poll every 1s. Unlike `API_BASE_URL`, a missing or malformed value
isn't fatal — it falls back to 2s. The overall poll timeout and consecutive-error
budget are constants on `PollingConfiguration`, not build settings.

Re-run `xcodegen generate` whenever `project.yml` changes, or after adding/removing/moving
source files (Xcode's file list is derived from what's on disk, not tracked incrementally in
`project.yml`).

The generated `.xcodeproj` is gitignored — see [ADR 0019](../docs/adr/0019-xcodegen-over-committed-xcodeproj.md)
for why.

### Signing

Both targets use automatic signing (`CODE_SIGN_STYLE: Automatic` in `project.yml`) with the team
from `Config/Local.xcconfig`, so Xcode creates and renews certificates and provisioning profiles itself.

- **Simulator:** any team works, and `CODE_SIGNING_ALLOWED=NO` builds don't sign at all.
  `Config/Local.xcconfig` must still exist, since XcodeGen errors without it.
- **Physical device:** the app declares the Push Notifications capability (`aps-environment` in
  `BackingTrack.entitlements`) for APNs ([ADR 0039](../docs/adr/0039-apns-push-from-api-on-terminal-job-state.md)).
  A free personal team can't provision that capability, so a device build needs a paid Apple Developer
  Program team.
- **TestFlight:** every push to `main` that touches `app/` (or the iOS workflows) archives the app and uploads it
  ([`ios-release.yml`](../.github/workflows/ios-release.yml), [ADR 0040](../docs/adr/0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md)).
  It uses cloud signing: `xcodebuild` authenticates with an App Store Connect API key and Apple holds the
  distribution certificate, so no certificates or profiles are stored in the repo or in CI secrets.
  TestFlight builds last 90 days. `MARKETING_VERSION` is bumped by hand in `project.yml`; the build number is
  the CI run number plus an offset of 100.
- **Version tags:** the maintainer tags the merge commit that bumps `MARKETING_VERSION` as `ios-v<version>`
  (e.g. `ios-v0.7.3`), mirroring the API's `api-v*` tags. The tag is a label, not the source of the version, so
  [`ios-tag-check.yml`](../.github/workflows/ios-tag-check.yml) fails any `ios-v*` tag that disagrees with
  `project.yml`.

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
[`api-ci.yml`](../.github/workflows/api-ci.yml) (the `api/` pipeline), so `api/`-only changes don't
wait on a macOS runner.
