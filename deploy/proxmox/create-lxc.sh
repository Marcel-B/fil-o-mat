#!/usr/bin/env bash
# Legt auf einem Proxmox-Host einen Debian-12-LXC an und startet darin das Fil-o-mat-Backend (Docker).
#
# Aufruf in der Proxmox-Shell (als root):
#   bash create-lxc.sh
# Einstellungen über Umgebungsvariablen, z. B.:
#   CTID=150 IP=192.168.1.50/24 GATEWAY=192.168.1.1 BRANCH=main bash create-lxc.sh
set -euo pipefail

CTID="${CTID:-$(pvesh get /cluster/nextid)}"
CT_HOSTNAME="${CT_HOSTNAME:-filomat}"
STORAGE="${STORAGE:-local-lvm}"            # Speicher für die Container-Disk
TEMPLATE_STORAGE="${TEMPLATE_STORAGE:-local}"
BRIDGE="${BRIDGE:-vmbr0}"
IP="${IP:-dhcp}"                           # oder z. B. 192.168.1.50/24
GATEWAY="${GATEWAY:-}"                     # nur bei fester IP nötig
DISK_GB="${DISK_GB:-8}"
MEMORY_MB="${MEMORY_MB:-1024}"
CORES="${CORES:-2}"
REPO_URL="${REPO_URL:-https://github.com/Marcel-B/fil-o-mat.git}"
BRANCH="${BRANCH:-main}"
GITHUB_TOKEN="${GITHUB_TOKEN:-}"           # nur bei privatem Repository
FILOMAT_API_KEY="${FILOMAT_API_KEY:-}"     # optional, dann müssen die Apps ihn mitschicken
PORT="${PORT:-8080}"

info() { echo -e "\e[1;34m==>\e[0m $*"; }
fail() { echo -e "\e[1;31mFehler:\e[0m $*" >&2; exit 1; }

command -v pct >/dev/null || fail "Das Skript muss auf dem Proxmox-Host laufen (pct fehlt)."
[[ $EUID -eq 0 ]] || fail "Bitte als root ausführen."
pct status "$CTID" &>/dev/null && fail "Container $CTID existiert bereits. Andere CTID wählen, z. B. CTID=151."

info "Debian-12-Vorlage suchen"
pveam update >/dev/null
TEMPLATE="$(pveam available --section system | awk '/debian-12-standard/ {print $2}' | sort -V | tail -n1)"
[[ -n "$TEMPLATE" ]] || fail "Keine debian-12-standard-Vorlage gefunden."
if ! pveam list "$TEMPLATE_STORAGE" | grep -q "$TEMPLATE"; then
  info "Vorlage $TEMPLATE herunterladen"
  pveam download "$TEMPLATE_STORAGE" "$TEMPLATE"
fi

NET="name=eth0,bridge=${BRIDGE},ip=${IP}"
[[ -n "$GATEWAY" ]] && NET="${NET},gw=${GATEWAY}"

info "Container $CTID ($CT_HOSTNAME) anlegen"
# nesting und keyctl braucht Docker in einem unprivilegierten Container
pct create "$CTID" "${TEMPLATE_STORAGE}:vztmpl/${TEMPLATE}" \
  --hostname "$CT_HOSTNAME" \
  --cores "$CORES" \
  --memory "$MEMORY_MB" \
  --swap 512 \
  --rootfs "${STORAGE}:${DISK_GB}" \
  --net0 "$NET" \
  --unprivileged 1 \
  --features nesting=1,keyctl=1 \
  --onboot 1 \
  --start 1

info "Auf Netzwerk warten"
for _ in $(seq 1 30); do
  pct exec "$CTID" -- getent hosts deb.debian.org &>/dev/null && break
  sleep 2
done
pct exec "$CTID" -- getent hosts deb.debian.org &>/dev/null || fail "Container $CTID hat kein Netzwerk."

CLONE_URL="$REPO_URL"
if [[ -n "$GITHUB_TOKEN" ]]; then
  CLONE_URL="${REPO_URL/https:\/\//https://x-access-token:${GITHUB_TOKEN}@}"
fi

info "Docker installieren und Backend starten (dauert ein paar Minuten)"
pct exec "$CTID" -- env \
  CLONE_URL="$CLONE_URL" REPO_URL="$REPO_URL" BRANCH="$BRANCH" \
  FILOMAT_API_KEY="$FILOMAT_API_KEY" PORT="$PORT" \
  bash -euo pipefail -c '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl git >/dev/null
    curl -fsSL https://get.docker.com | sh >/dev/null

    git clone --branch "$BRANCH" "$CLONE_URL" /opt/fil-o-mat
    # Token nicht in der Git-Konfiguration liegen lassen
    git -C /opt/fil-o-mat remote set-url origin "$REPO_URL"

    cat > /opt/fil-o-mat/backend/.env <<ENV
FILOMAT_API_KEY=$FILOMAT_API_KEY
FILOMAT_PORT=$PORT
ENV
    chmod 600 /opt/fil-o-mat/backend/.env

    cd /opt/fil-o-mat/backend
    docker compose up -d --build

    for _ in $(seq 1 30); do
      curl -fs "http://localhost:$PORT/health" >/dev/null && exit 0
      sleep 2
    done
    docker compose logs --tail 50
    exit 1
  ' || fail "Backend startet nicht, Logs stehen oben."

CT_IP="$(pct exec "$CTID" -- hostname -I | awk '{print $1}')"
info "Fertig"
echo "  Container:  $CTID ($CT_HOSTNAME)"
echo "  Backend:    http://${CT_IP}:${PORT}"
[[ -n "$FILOMAT_API_KEY" ]] && echo "  API-Schlüssel ist gesetzt."
echo
echo "  Diese Adresse in der Android- und iOS-App eintragen."
echo "  Aktualisieren: pct exec $CTID -- bash -c 'cd /opt/fil-o-mat && git pull && cd backend && docker compose up -d --build'"
