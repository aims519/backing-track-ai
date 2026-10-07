# 0047. Roll back automatically when post-deploy checks fail, and prune old releases after a good deploy

Status: Accepted

## Context

`deploy.yml` copies a release to `/opt/backingtrack/releases/<sha>`, repoints the `current`
symlink at it, restarts `backingtrack-api.service`, then polls `/healthz/live` and
`/healthz/ready`. When a check failed the job exited 1 and nothing else happened: the broken
release stayed live, and `Restart=on-failure` in the unit only restarted that same broken binary.
Recovery meant noticing the red run and SSHing in to repoint the symlink by hand.

Every push to `main` that touches `api/**` also adds a release directory, and nothing ever removed
one. The Pi's storage is small and the Native AOT output is not.

The constraints are a single Pi, one service on a fixed port (`tailscale serve` proxies to
`:5080`, ADR 0042), and a `ci-deploy` user whose only passwordless sudo is
`systemctl restart backingtrack-api.service` (ADR 0002).

## Decision

**Rollback.** Before the swap, a step reads `readlink` on `current` and saves it as a step output.
A `Roll back to previous release` step runs on `failure() && <previous path is not empty>`. It
checks the old release directory still exists, repoints `current` at it with `ln -sfn`, restarts
the service, and polls `/healthz/live`. The failed step has already marked the job red, so the
job still fails.

The trigger is `failure()`, not "readiness failed". A failed swap or restart, liveness, readiness,
and any step added after them all roll back. What `/healthz/ready` means can change without
touching the rollback.

**Pruning.** After both checks pass, `scripts/prune-releases.sh` is piped over SSH
(`bash -s < script`). It keeps the newest `KEEP_RELEASES` (5) directories by mtime plus whichever
one `current` points at, and deletes the rest. It lives in `scripts/` and not inline in the YAML,
so it can be run and read on its own.

## Alternatives considered

- **Keep rolling back by hand.** The status quo. A bad release is live until the owner notices the
  failed run, and a deploy that fails at 2am stays broken until morning.
- **Rely on systemd `Restart=on-failure`.** It restarts the same binary from the same symlink, so
  a release that crashes on startup just crash-loops.
- **Blue/green: start the new release on a second port, switch over once healthy.** Avoids
  serving a broken release at all, but needs a proxy or a `tailscale serve` target switch and a
  second unit. That is a lot of moving parts for a one-user service on one Pi, where a few
  seconds of bad service on a failed deploy is acceptable.
- **Roll back on readiness failure only.** Readiness is the later of the two steps, so a liveness
  failure would exit before it and leave the broken release live.
- **Prune by age instead of count.** Disk use then depends on deploy frequency, and a quiet spell
  could delete the previous release, the one a rollback needs.
- **Prune with a cron job or `logrotate` on the Pi.** Keeps the logic off the deploy path, but it
  is machine state outside the repo that `pi-bootstrap.sh` would have to install, and it can't
  know which release is live as precisely as the deploy that just verified it.
- **Prune before the deploy, or on every run.** A failed deploy could then delete the release it
  needs to fall back to. Pruning only after both checks pass means the previous release is always
  present while a rollback might be needed.
- **Delete the failed release when rolling back.** Tidier, but it removes the binary that needs
  inspecting. It is left for a later prune to remove.

## Consequences

- **A healthy release can be rolled back.** When OpenTelemetry is enabled, `/healthz/ready` is
  made up only of the OTel collector check (`OtelCollectorHealthCheck`). If the collector is down
  during a deploy, the new release is rolled back although it is fine. #123
  changes what readiness means, and whatever it adds also becomes a rollback trigger. Readiness
  should only contain checks whose failure should block a release.
- **Only the binary is restored.** The rollback repoints a symlink. It does not undo anything the
  failed release did to persisted state such as files under `/mnt/ssd/backing-tracks`. A release
  that migrates data in a way the previous one cannot read needs handling the rollback does not
  provide.
- **One level of fallback.** Rollback goes back one release. If the previous release is also bad,
  or a regression passes both checks, recovery is manual: the other four kept releases are for
  that.
- **Failed releases count towards the five.** A run of failed deploys can push good older releases
  out of the window. The release `current` points at is protected regardless.
- **Rollback is brief downtime, not zero.** The service is restarted onto the new release, fails,
  and is restarted again, so a failed deploy means a short outage.
- **The first deploy has nothing to fall back to.** With no `current` symlink there is no previous
  path, so the rollback is skipped and the job just fails.
- **`ln -sfn` is not atomic.** `current` briefly doesn't exist during the swap. It is harmless
  because the service is restarted straight after, and the deploy already worked this way.
- **The script is read from the checkout.** The `deploy` job now does a sparse checkout of
  `scripts/`. Editing `prune-releases.sh` alone does not trigger a deploy, because the
  workflow's path filter is `api/**`; the change takes effect on the next API deploy.
