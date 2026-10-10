# 0003. Use the Pi's numeric Tailscale IP, not MagicDNS, in the deploy workflow

Status: Accepted

## Context

The GitHub Actions deploy workflow (`.github/workflows/deploy.yml`) needs to reach the Pi for
`ssh-keyscan`, SSH/SCP, and the post-deploy health check, once the ephemeral runner has joined
the tailnet via `tailscale/github-action`. Tailscale offers two ways to address a device:
MagicDNS hostnames (`<pi>` / `<pi>.<tailnet>.ts.net`) or the device's stable numeric
tailnet IP (`100.x.y.z`).

## Decision

Address the Pi by its numeric Tailscale IP, stored as the `PI_TAILSCALE_IP` repo variable,
everywhere in the workflow.

## Alternatives considered

MagicDNS hostname — rejected for CI use specifically (still fine for interactive/manual use,
e.g. checking the health endpoint from a browser). Immediately after `tailscale/github-action`
brings the ephemeral runner onto the tailnet, there's a window where WireGuard connectivity is
up but `tailscaled` hasn't necessarily finished syncing the tailnet's MagicDNS configuration.
A lookup that lands in that window fails or resolves stale — an intermittent, hard-to-reproduce
CI failure that passes on retry. The numeric IP has no such dependency and is usable the
instant the tunnel is established.

## Consequences

If the Pi's Tailscale IP ever changes (re-authenticating the device, or leaving and rejoining
the tailnet), `PI_TAILSCALE_IP` goes stale and needs a manual update — a MagicDNS name would
have kept working transparently through that change. In practice Tailscale IPs are stable for
the lifetime of a device unless deliberately reset, so this is a low-frequency cost traded for
removing a real, recurring source of CI flakiness.
