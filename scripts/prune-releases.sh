#!/usr/bin/env bash
# Deletes old release directories on the Pi. Piped over SSH by deploy.yml after a successful deploy:
#   ssh ci-deploy@pi bash -s -- <keep> <releases-dir> <current-link> < prune-releases.sh
#
# Keeps the <keep> newest releases, plus whichever one <current-link> points at, even if it is old
# (after a rollback the live release may not be among the newest).

set -euo pipefail

KEEP="$1"
RELEASES_DIR="$2"
CURRENT_LINK="$3"

LIVE_RELEASE="$(readlink "$CURRENT_LINK")"

cd "$RELEASES_DIR"

# releases/ only holds one directory per deployed SHA, so ls -t (newest first) is enough.
OLD_RELEASES="$(ls -t | tail -n +$((KEEP + 1)))"

for RELEASE in $OLD_RELEASES; do
  if [[ "$RELEASES_DIR/$RELEASE" == "$LIVE_RELEASE" ]]; then
    echo "Keeping $RELEASE (live)"
    continue
  fi
  echo "Pruning $RELEASE"
  rm -rf "$RELEASES_DIR/$RELEASE"
done
