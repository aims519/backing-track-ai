# 0042. HTTPS to the API via `tailscale serve`; App Transport Security left on

Status: Accepted

Supersedes [0022](0022-plaintext-http-over-tailscale-ats-disabled.md).

## Context

[ADR 0022](0022-plaintext-http-over-tailscale-ats-disabled.md) disabled App Transport Security
(`NSAllowsArbitraryLoads = true`) because the API only spoke plain HTTP and the app had to reach
it on the Pi. It rejected terminating TLS on the Pi "for now" as too much setup (cert renewal, a
Kestrel HTTPS config in the AOT build, pinning), and said to revisit it if the circumstances
changed. Two things did:

- **Builds now go to TestFlight on every push to `main`** ([ADR 0040](0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md)).
  ADR 0022's "no App Store distribution" justification no longer fully holds, and a blanket ATS
  opt-out is the sort of thing App Review asks about if the app ever goes beyond internal testing.
- **`tailscale serve` removes the setup cost that made TLS look expensive.** It terminates HTTPS on
  the Pi with a certificate Tailscale provisions and renews for the node's `*.ts.net` name, and
  proxies to the local HTTP listener. The API itself needs no TLS code.

## Decision

Run `tailscale serve` on the Pi to publish the API over HTTPS at its MagicDNS name
(`https://<pi>.<tailnet>.ts.net`), proxying to the API's existing plain-HTTP listener. The
client treats it as an ordinary HTTPS URL: the `NSAppTransportSecurity` dictionary is removed from
`Info.plist`, so ATS is back at its default, and the Release base URL (`Config/Local.xcconfig`,
and the `IOS_API_BASE_URL` repository variable for CI) is the `https://` address.

Debug builds keep `http://localhost:5198`. ATS doesn't apply to unqualified hostnames such as
`localhost`, so the simulator and a local `dotnet run` need no exception.

## Alternatives considered

- **Keep ATS disabled (ADR 0022).** Worked, and the WireGuard tunnel did encrypt the traffic. It
  was rejected now because the cost it avoided is gone, and it left a broad exception that covers
  every URL the app loads rather than just the API.
- **A scoped `NSExceptionDomains` entry for the Pi's host.** Still impractical for the reason in
  ADR 0022: the host lives in per-machine config, so a committed `Info.plist` can't name it. It
  would also have kept the API on plain HTTP.
- **Kestrel terminating TLS with a Tailscale-issued certificate** (`tailscale cert`, loaded by the
  API). Same trusted certificate, but the API would own loading it, renewing it and restarting on
  rotation, in a Native AOT build. `tailscale serve` does all of that outside the application.
- **A self-signed certificate pinned in the app.** Rejected: it adds pinning code and a rotation
  story to the client, where a publicly trusted certificate needs neither.

## Consequences

- **Transport is now authenticated at the TLS layer, not only by the tunnel.** The tunnel still
  encrypts everything, and iOS now also validates the server's certificate for the API. A future
  feature that loads third-party content gets ATS protection by default.
- **The HTTPS endpoint depends on `tailscale serve` configuration that lives on the Pi.**
  `scripts/pi-bootstrap.sh` runs `tailscale serve --bg 5080`, so a rebuilt Pi gets it back, but
  it needs the Pi already on the tailnet with HTTPS certificates enabled in the admin console.
  Both are still manual steps.
- **The API still listens on plain HTTP** (`ASPNETCORE_URLS=http://0.0.0.0:5080`). The deploy
  workflow's health checks still use it directly over the tunnel. Anything on the tailnet can
  still reach that port unencrypted at the application layer, though it remains inside WireGuard.
- **Tailscale HTTPS certificates are logged to public Certificate Transparency logs**, so the
  node's `*.ts.net` hostname (including the tailnet name) becomes publicly discoverable. The
  endpoint is still reachable only from inside the tailnet.
- **The base URL changes everywhere it's set.** The CI variable, each developer's
  `Local.xcconfig`, and the example file all had to move to `https://`. A stale `http://` value in
  a Release build now fails at runtime, because ATS blocks it, rather than working.
