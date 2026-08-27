#!/usr/bin/env bash
# Runs a real Native AOT publish locally to catch trimming/AOT-unsafety (IL2026, IL3050, etc.)
# before pushing — a normal `dotnet build`/`dotnet test` never exercises this analyzer; only
# `dotnet publish -p:PublishAot=true` does, which is why a warning like this can slip through
# and only break the actual Pi deploy. See docs/adr/0016-aot-safe-json-typeinfo-overload.md for
# the incident that motivated this script.
#
# Publishes for the *host* RID (not the Pi's linux-arm64), deliberately: the AOT/trimming
# analyzer isn't RID-specific — it flags reflection-unsafe code regardless of target platform —
# and using the host RID avoids needing the linux-arm64 cross-toolchain locally. Takes well
# under a minute.
#
# Usage: scripts/check-aot.sh [rid]
#   An explicit RID overrides auto-detection, e.g. scripts/check-aot.sh linux-x64

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$(mktemp -d)"
trap 'rm -rf "$OUT_DIR"' EXIT

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os-$arch" in
    Darwin-arm64) echo "osx-arm64" ;;
    Darwin-x86_64) echo "osx-x64" ;;
    Linux-x86_64) echo "linux-x64" ;;
    Linux-aarch64|Linux-arm64) echo "linux-arm64" ;;
    *)
      echo "Couldn't auto-detect a RID for $os-$arch — pass one explicitly, e.g.:" >&2
      echo "  scripts/check-aot.sh linux-x64" >&2
      exit 1
      ;;
  esac
}

RID="${1:-$(detect_rid)}"

echo "== Native AOT publish check ($RID) =="
dotnet publish "$REPO_ROOT/api/src/BackingTrack.Api" \
  -c Release \
  -r "$RID" \
  -p:PublishAot=true \
  --self-contained \
  -o "$OUT_DIR"

echo "== AOT/trimming analysis passed clean =="
