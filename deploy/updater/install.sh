#!/usr/bin/env bash
# Richtet im LXC den Updater ein: die API zeigt dann neue Commits an und kann das Update auslösen.
#   bash /opt/fil-o-mat/deploy/updater/install.sh
# Danach einmal neu starten, damit der Container den Ordner sieht:
#   cd /opt/fil-o-mat/backend && docker compose up -d
# --units-only erneuert nur die systemd-Units (ruft filomat-update.sh nach jedem Update auf).
set -euo pipefail

REPO="$(cd "$(dirname "$(readlink -f "$0")")/../.." && pwd)"
STATE_DIR=/var/lib/filomat-update
SCRIPT="$REPO/deploy/updater/filomat-update.sh"
APP_UID=1654   # Benutzer "app" im aspnet-Image
INTERVAL="${FILOMAT_UPDATE_INTERVAL:-15min}"

[[ $EUID -eq 0 ]] || { echo "Bitte als root ausführen." >&2; exit 1; }

install_units() {
  cat > /etc/systemd/system/filomat-update-check.service <<UNIT
[Unit]
Description=Fil-o-mat: auf neue Commits prüfen
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$SCRIPT check
UNIT

  cat > /etc/systemd/system/filomat-update-check.timer <<UNIT
[Unit]
Description=Fil-o-mat: regelmäßig auf neue Commits prüfen

[Timer]
OnBootSec=2min
OnUnitActiveSec=$INTERVAL
RandomizedDelaySec=60

[Install]
WantedBy=timers.target
UNIT

  cat > /etc/systemd/system/filomat-update.service <<UNIT
[Unit]
Description=Fil-o-mat: Auftrag der API ausführen (prüfen oder aktualisieren)
After=docker.service network-online.target

[Service]
Type=oneshot
ExecStart=$SCRIPT request
TimeoutStartSec=30min
UNIT

  cat > /etc/systemd/system/filomat-update.path <<UNIT
[Unit]
Description=Fil-o-mat: auf Aufträge der API warten

[Path]
PathExists=$STATE_DIR/request

[Install]
WantedBy=paths.target
UNIT

  systemctl daemon-reload
}

if [[ "${1:-}" == --units-only ]]; then
  install_units
  exit 0
fi

command -v jq >/dev/null || { apt-get update -qq && apt-get install -y -qq jq >/dev/null; }

mkdir -p "$STATE_DIR"
chown "$APP_UID" "$STATE_DIR"
chmod 755 "$STATE_DIR"

# Ordner in den Container einbinden
ENV_FILE="$REPO/backend/.env"
touch "$ENV_FILE"
chmod 600 "$ENV_FILE"
if ! grep -q '^COMPOSE_FILE=' "$ENV_FILE"; then
  echo "COMPOSE_FILE=docker-compose.yml:docker-compose.updater.yml" >> "$ENV_FILE"
fi

install_units
systemctl enable --now filomat-update-check.timer filomat-update.path
"$SCRIPT" check || true

echo "Updater eingerichtet. Stand: $STATE_DIR/status.json"
