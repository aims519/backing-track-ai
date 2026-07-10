# 0002. Tailscale OAuth (tag-scoped) and a shared Pi deploy user

Status: Accepted

## Context

Setting up the GitHub Actions → Pi deploy pipeline required two related decisions: how the
ephemeral CI runner authenticates onto the tailnet, and what user account(s) it uses once
connected. The Pi is expected to host more than one portfolio project over time, not just this
one.

## Decision

- **Tailscale auth**: a Tailscale OAuth client scoped to a tag (`tag:ci`), not a long-lived
  reusable auth key. Each CI run mints its own short-lived, tag-restricted node.
- **Pi user model**: a single shared system user, `ci-deploy`, used by every project's CI to
  SSH in and swap release binaries — rather than a separate deploy user per project. However,
  each project gets its **own SSH keypair**; all public halves are appended to `ci-deploy`'s
  `authorized_keys`, and each project's private half lives only in that project's own GitHub
  repo secrets. The service itself runs under its own unprivileged per-project runtime user
  (`backingtrack-api` for this project), separate from `ci-deploy`.

## Alternatives considered

- **Reusable Tailscale auth key** — rejected. It's a long-lived bearer secret; if it leaked
  (a misconfigured log, a compromised build dependency) it could join arbitrary new devices to
  the tailnet until manually revoked. A tag-scoped OAuth client lets an ACL restrict `tag:ci`
  devices to only reach the Pi on the ports CI needs, nothing else on the tailnet.
- **A separate deploy user per project** (e.g. `btdeploy-backingtrack`, `btdeploy-nextproject`,
  ...) — rejected as the default. For a solo Pi where every project is the same person's own
  work, multiplying system users for what's fundamentally one trust boundary is bookkeeping
  overhead without real isolation benefit. The one place isolation *does* matter — one leaked
  project's credential shouldn't compromise another project's deploy access — is handled by
  giving each project its own key rather than its own user; revoking one project's line from
  `authorized_keys` doesn't touch any other project.
- **One shared key across all projects** — rejected. A single private key living in every
  project's GitHub secrets means a leak in any one repo's Actions logs compromises deploy
  access for all of them simultaneously. Per-project keys under a shared user avoid this while
  still keeping user management simple.

## Consequences

Adding a second project later means: generate a new keypair for it, append its public half to
`ci-deploy`'s existing `authorized_keys`, add its own runtime user and a new explicit sudoers
line for its own service name (sudoers is scoped per exact unit name, not a wildcard — see the
bootstrap script), and give it its own release directory under `/opt/`. No changes needed to
`ci-deploy` itself or to this project's key/config. The tradeoff is that `ci-deploy`'s
`authorized_keys` file becomes a small manually-maintained list as projects are added — fine at
the scale of a personal portfolio Pi, would need automation if this ever grew beyond a handful
of projects.
