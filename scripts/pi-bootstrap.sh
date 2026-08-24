#!/usr/bin/env bash
# One-time setup for the BackingTrack API on the Pi. Run manually, once, with sudo:
#   sudo bash pi-bootstrap.sh
#
# Idempotent: safe to re-run if a step failed partway through.
#
# What this does NOT do: start the service (no binary exists yet — that happens on the
# first CI deploy), or touch Tailscale/GitHub — those are separate manual steps.

set -euo pipefail

DEPLOY_USER="ci-deploy"
RUNTIME_USER="backingtrack-api"
APP_ROOT="/opt/backingtrack"
ENV_FILE="$APP_ROOT/shared/api.env"
UNIT_FILE="/etc/systemd/system/backingtrack-api.service"
SUDOERS_FILE="/etc/sudoers.d/ci-deploy-backingtrack"

if [[ $EUID -ne 0 ]]; then
  echo "Run this as root (sudo bash pi-bootstrap.sh)." >&2
  exit 1
fi

echo "== Creating shared deploy user: $DEPLOY_USER =="
if ! id "$DEPLOY_USER" &>/dev/null; then
  useradd --create-home --shell /bin/bash "$DEPLOY_USER"
else
  echo "$DEPLOY_USER already exists, skipping."
fi

echo "== Creating per-project runtime user: $RUNTIME_USER =="
if ! id "$RUNTIME_USER" &>/dev/null; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$RUNTIME_USER"
else
  echo "$RUNTIME_USER already exists, skipping."
fi

echo "== Scoping passwordless sudo for the restart command only =="
echo "$DEPLOY_USER ALL=(root) NOPASSWD: /usr/bin/systemctl restart backingtrack-api.service" > "$SUDOERS_FILE"
chmod 440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

echo "== Creating directory structure =="
mkdir -p "$APP_ROOT/releases" "$APP_ROOT/shared"
# ci-deploy owns $APP_ROOT itself (not just releases/) because it needs to create/update
# the "current" symlink directly inside it - that requires write access on the parent
# directory, not on releases/.
chown "$DEPLOY_USER:$DEPLOY_USER" "$APP_ROOT"
chown "$DEPLOY_USER:$DEPLOY_USER" "$APP_ROOT/releases"
chown "$RUNTIME_USER:$RUNTIME_USER" "$APP_ROOT/shared"
chmod 755 "$APP_ROOT"
# releases/ must be traversable by backingtrack-api (the runtime user, not the owner and not
# in ci-deploy's group) so systemd can chdir into the release directory the "current" symlink
# points at. No secrets live here (those are in shared/, kept at 750/600), so world-readable
# is fine.
chmod 755 "$APP_ROOT/releases"
chmod 750 "$APP_ROOT/shared"

echo "== Writing environment file (edit $ENV_FILE afterwards for real secrets) =="
if [[ ! -f "$ENV_FILE" ]]; then
  cat > "$ENV_FILE" <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5080
EOF
  chown "$RUNTIME_USER:$RUNTIME_USER" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
else
  echo "$ENV_FILE already exists, leaving it alone."
fi

echo "== Writing systemd unit =="
cat > "$UNIT_FILE" <<EOF
[Unit]
Description=BackingTrack API
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$RUNTIME_USER
Group=$RUNTIME_USER
WorkingDirectory=$APP_ROOT/current
ExecStart=$APP_ROOT/current/BackingTrack.Api
Restart=on-failure
RestartSec=2
EnvironmentFile=$ENV_FILE
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=$APP_ROOT/current /mnt/ssd/backing-tracks
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable backingtrack-api.service

echo
echo "== Bootstrap done. Remaining manual steps (not done by this script): =="
echo "1. On your dev machine, generate a deploy keypair for this project:"
echo "     ssh-keygen -t ed25519 -f ./backingtrack-deploy-key -N \"\""
echo "2. Append the PUBLIC half to this user's authorized_keys on the Pi:"
echo "     sudo mkdir -p /home/$DEPLOY_USER/.ssh"
echo "     sudo tee -a /home/$DEPLOY_USER/.ssh/authorized_keys < backingtrack-deploy-key.pub"
echo "     sudo chown -R $DEPLOY_USER:$DEPLOY_USER /home/$DEPLOY_USER/.ssh"
echo "     sudo chmod 700 /home/$DEPLOY_USER/.ssh"
echo "     sudo chmod 600 /home/$DEPLOY_USER/.ssh/authorized_keys"
echo "3. Set the PRIVATE half as a GitHub secret, then delete the local copy:"
echo "     gh secret set DEPLOY_SSH_PRIVATE_KEY < ./backingtrack-deploy-key"
echo "     rm ./backingtrack-deploy-key ./backingtrack-deploy-key.pub"
echo "4. There is no running service yet — that's expected. The first CI deploy will"
echo "   populate $APP_ROOT/current and start it for the first time."
