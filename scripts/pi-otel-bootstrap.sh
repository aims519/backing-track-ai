#!/usr/bin/env bash
# One-time setup for the OpenTelemetry Collector on the Pi. Run manually, once, with sudo:
#   sudo bash pi-otel-bootstrap.sh
#
# Idempotent: safe to re-run if a step failed partway through.
#
# Shared, Pi-wide infrastructure — not scoped to any one project, unlike pi-bootstrap.sh's
# /opt/backingtrack. Any project's app on this Pi can send OTLP data to localhost:4317/4318.
#
# What this does NOT do: set GRAFANA_API_TOKEN in the env file — that's a manual step, done
# once via SSH, never committed or pasted anywhere this script (or its output) can leak it.

set -euo pipefail

COLLECTOR_VERSION="0.157.0"
RUNTIME_USER="otel-collector"
APP_ROOT="/opt/otel-collector"
ENV_FILE="$APP_ROOT/otel-collector.env"
CONFIG_FILE="$APP_ROOT/otel-collector-config.yaml"
UNIT_FILE="/etc/systemd/system/otel-collector.service"

if [[ $EUID -ne 0 ]]; then
  echo "Run this as root (sudo bash pi-otel-bootstrap.sh)." >&2
  exit 1
fi

echo "== Creating runtime user: $RUNTIME_USER =="
if ! id "$RUNTIME_USER" &>/dev/null; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$RUNTIME_USER"
else
  echo "$RUNTIME_USER already exists, skipping."
fi

echo "== Installing otelcol-contrib v$COLLECTOR_VERSION =="
if ! command -v otelcol-contrib &>/dev/null; then
  ARCH="$(dpkg --print-architecture)"
  TMP_DEB="$(mktemp --suffix=.deb)"
  curl -fsSL -o "$TMP_DEB" \
    "https://github.com/open-telemetry/opentelemetry-collector-releases/releases/download/v${COLLECTOR_VERSION}/otelcol-contrib_${COLLECTOR_VERSION}_linux_${ARCH}.deb"
  dpkg -i "$TMP_DEB"
  rm -f "$TMP_DEB"
  # The .deb installs its own systemd unit and starts it against the default config;
  # we replace both below, so stop it now rather than run two copies briefly.
  systemctl stop otelcol-contrib.service 2>/dev/null || true
  systemctl disable otelcol-contrib.service 2>/dev/null || true
else
  echo "otelcol-contrib already installed, skipping."
fi

echo "== Creating directory structure =="
mkdir -p "$APP_ROOT"
chown "$RUNTIME_USER:$RUNTIME_USER" "$APP_ROOT"
chmod 750 "$APP_ROOT"

echo "== Writing collector config =="
cp "$(dirname "$0")/otel-collector/otel-collector-config.yaml" "$CONFIG_FILE"
chown "$RUNTIME_USER:$RUNTIME_USER" "$CONFIG_FILE"
chmod 640 "$CONFIG_FILE"

echo "== Writing environment file scaffold (edit $ENV_FILE afterwards for the real token) =="
if [[ ! -f "$ENV_FILE" ]]; then
  cat > "$ENV_FILE" <<'EOF'
GRAFANA_API_TOKEN=REPLACE_ME
EOF
  chown "$RUNTIME_USER:$RUNTIME_USER" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
else
  echo "$ENV_FILE already exists, leaving it alone."
fi

echo "== Writing systemd unit =="
cat > "$UNIT_FILE" <<EOF
[Unit]
Description=OpenTelemetry Collector
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$RUNTIME_USER
Group=$RUNTIME_USER
ExecStart=/usr/bin/otelcol-contrib --config=$CONFIG_FILE
Restart=on-failure
RestartSec=2
EnvironmentFile=$ENV_FILE
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable otel-collector.service

echo
echo "== Bootstrap done. Remaining manual step (not done by this script): =="
echo "1. Set the real token in $ENV_FILE (still holding REPLACE_ME right now):"
echo "     sudo nano $ENV_FILE"
echo "2. Start the service:"
echo "     sudo systemctl start otel-collector.service"
echo "     sudo systemctl status otel-collector.service"
