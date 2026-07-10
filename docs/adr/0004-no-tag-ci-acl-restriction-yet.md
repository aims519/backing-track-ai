# 0004. Defer restricting tag:ci's Tailscale ACL grant

Status: Accepted

## Context

ADR 0002 called for a Tailscale ACL grant restricting `tag:ci` devices (the ephemeral CI
runner) to only reach the Pi on ports 22 and 5080 — nothing else on the tailnet. When actually
configuring this in the Tailscale admin console, the existing policy turned out to already
contain a blanket allow-all grant:
```json
{"src": ["*"], "dst": ["*"], "ip": ["*"]}
```
Tailscale grants are purely additive (allow-lists, no deny concept) — a connection is permitted
if *any* grant allows it. Adding a narrower `tag:ci`-specific grant alongside this blanket rule
would change nothing in practice, since the wildcard already permits `tag:ci` (or any device) to
reach everything.

## Decision

Leave the blanket allow-all grant in place for now. `tag:ci` exists (for the OAuth client to
scope itself to) but carries no additional restriction beyond what every other device on the
tailnet already has.

## Alternatives considered

Removing the blanket allow-all and writing explicit per-path grants (phone→Pi, laptop→Pi, the
restrictive `tag:ci`→Pi:22,5080 grant, etc.) — rejected for now. This is a genuinely bigger
change than it first appears: the tailnet currently has three devices (phone, laptop, Pi), all
personally owned and already mutually trusted, and locking down the policy properly means
enumerating every legitimate path currently relying on the implicit allow-all, not just adding
one rule for CI. Doing it as a side effect of setting up this one pipeline risked either
breaking existing device connectivity or producing a rushed, incomplete policy.

## Consequences

The CI SSH key is still meaningfully scoped by two other layers, independent of Tailscale ACLs:
the `authorized_keys` entry only grants login as the unprivileged `ci-deploy` user, and the
sudoers drop-in from `scripts/pi-bootstrap.sh` limits what that login can do as root to one
exact command. A compromised CI credential could still reach other tailnet devices at the
network level (since ACLs aren't restricting it), but could not do anything on them without
also compromising their own auth. If the tailnet later grows to include less-trusted devices
(a shared/borrowed machine, a third party's device), this decision should be revisited —
properly locking down the ACL policy at that point, not just for `tag:ci`.
