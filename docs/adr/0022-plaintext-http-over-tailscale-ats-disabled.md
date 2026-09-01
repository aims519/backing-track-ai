# 0022. Plaintext HTTP to the API over Tailscale; App Transport Security disabled

Status: Accepted

## Context

Wiring the iOS app to the backend (the "Hook up generate track API call" task) means the app
now makes real HTTP requests to the API. Two things about the deployment shape decide how:

- The API listens on plain HTTP. Locally it's `http://localhost:5198` (`dotnet run`); on the Pi
  it's `http://0.0.0.0:5080` behind the systemd unit (`scripts/pi-bootstrap.sh`). Nothing
  terminates TLS — not the Pi, not a reverse proxy (there isn't one).
- The Pi is only reachable over Tailscale (WireGuard mesh VPN) — no port forwarding, no public
  DNS, no public exposure at all (see `docs/backing-track-generator-scope.md`, "Network
  exposure"). The phone reaches it by its Tailscale MagicDNS name.

iOS's App Transport Security (ATS) blocks cleartext HTTP by default. An app that makes a plain
`http://` request with stock ATS just gets a failed connection. So the app needs an ATS
decision, and it needs a way to know which base URL to use.

The base-URL selection is settled separately: a per-configuration `API_BASE_URL` build setting
(Debug defaults to localhost in `project.yml`; Release comes from the gitignored
`Config/Local.xcconfig`, like `DEVELOPMENT_TEAM`), surfaced as an Info.plist key. That part
isn't architecturally interesting. The ATS posture is.

## Decision

Disable ATS wholesale — `NSAppTransportSecurity` → `NSAllowsArbitraryLoads = true` in the app's
Info.plist — with a comment there pointing at this record.

## Alternatives considered

- **A scoped `NSExceptionDomains` entry** for localhost and the Pi's host instead of a blanket
  opt-out. This is the "right" way to narrow an ATS exception. Rejected as impractical here: the
  Pi's MagicDNS host lives in the per-machine `Config/Local.xcconfig` (it's tied to a tailnet,
  not the project — same reasoning as `DEVELOPMENT_TEAM` and as ADR 0003's numeric-IP choice for
  CI), so a committed Info.plist can't name it. Threading the hostname from the build setting
  into a generated Info.plist exception dict is doable but a lot of machinery for a single-user
  app. `NSAllowsLocalNetworking` would cover localhost but not the `.ts.net` host.
- **Terminate TLS on the Pi** — a self-signed cert pinned in the app, or a real certificate for
  the MagicDNS name (Tailscale can provision Let's Encrypt certs for `*.ts.net`). This removes
  the need for any ATS exception. Rejected for now: it's real setup and maintenance (cert
  renewal, a Kestrel HTTPS config in the AOT build, pinning logic or a trust store in the app)
  for a threat model that Tailscale already covers. Worth revisiting if the API ever leaves the
  tailnet.
- **Keep ATS on and accept that only `localhost` works.** Non-starter — the whole point is to
  reach the Pi from the phone.

## Consequences

- **The transport is not unencrypted in practice.** Every byte between the phone and the Pi goes
  through the Tailscale WireGuard tunnel, which encrypts and authenticates it. What's disabled is
  iOS's *check*, not the encryption. On an untrusted network the traffic is still protected by
  the VPN.
- **`NSAllowsArbitraryLoads` is broad** — it lifts ATS for *any* URL the app loads, not just the
  API. The app only ever talks to its own API, so the practical surface is nil, but a future
  feature that loads third-party content wouldn't get ATS's protection for free. If that happens,
  switch to `NSAllowsArbitraryLoads = false` plus explicit exception domains.
- **App Store review would reject this** without a justification string. Not a concern: no App
  Store distribution (`docs/backing-track-generator-scope.md`, Non-Goals). If that ever changes,
  this decision has to change with it — hence recording it rather than leaving it as an
  unexplained plist key.
- **The API staying HTTP-only is now a load-bearing assumption on the client side.** If the Pi
  moves to HTTPS later, the client's base URL and this ATS config both change; this record is the
  breadcrumb.
