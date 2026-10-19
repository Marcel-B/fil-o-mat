#!/usr/bin/env bash
# Macht das Backend in einem bestehenden Fil-o-mat-LXC über Tailscale erreichbar.
#
# Aufruf in der Proxmox-Shell (als root):
#   CTID=150 bash add-tailscale.sh
# Einstellungen über Umgebungsvariablen:
#   TS_AUTHKEY=tskey-auth-…   ohne Schlüssel zeigt Tailscale einen Login-Link an
#   TS_HOSTNAME=filomat       Name im Tailnet (Standard: Hostname des LXC)
#   TS_SERVE=1                zusätzlich HTTPS über "tailscale serve" (MagicDNS und HTTPS-Zertifikate im Tailnet an)
#
# Das Skript ist eigenständig, damit es auch ohne Checkout auf dem Proxmox-Host läuft.
# create-lxc.sh ruft es mit TAILSCALE=1 auch für neue Container auf.
set -euo pipefail

CTID="${CTID:-${1:-}}"
TS_AUTHKEY="${TS_AUTHKEY:-}"
TS_HOSTNAME="${TS_HOSTNAME:-}"
TS_SERVE="${TS_SERVE:-0}"

info() { echo -e "\e[1;34m==>\e[0m $*"; }
fail() { echo -e "\e[1;31mFehler:\e[0m $*" >&2; exit 1; }

command -v pct >/dev/null || fail "Das Skript muss auf dem Proxmox-Host laufen (pct fehlt)."
[[ $EUID -eq 0 ]] || fail "Bitte als root ausführen."
[[ -n "$CTID" ]] || fail "CTID fehlt, z. B. CTID=150 bash add-tailscale.sh"
pct status "$CTID" &>/dev/null || fail "Container $CTID gibt es nicht."

CONF="/etc/pve/lxc/${CTID}.conf"
TS_HOSTNAME="${TS_HOSTNAME:-$(sed -n 's/^hostname: //p' "$CONF")}"

# Ein unprivilegierter Container sieht /dev/net/tun nur, wenn es durchgereicht wird; ohne startet tailscaled nicht.
if ! grep -q '^lxc.mount.entry: /dev/net/tun' "$CONF"; then
  info "/dev/net/tun an Container $CTID durchreichen"
  cat >> "$CONF" <<CONF
lxc.cgroup2.devices.allow: c 10:200 rwm
lxc.mount.entry: /dev/net/tun dev/net/tun none bind,create=file
CONF
fi

if [[ "$(pct status "$CTID")" != *running* ]]; then
  info "Container $CTID starten"
  pct start "$CTID"
elif ! pct exec "$CTID" -- test -c /dev/net/tun; then
  info "Container $CTID neu starten, damit er /dev/net/tun sieht"
  pct reboot "$CTID"
fi

for _ in $(seq 1 30); do
  pct exec "$CTID" -- getent hosts pkgs.tailscale.com &>/dev/null && break
  sleep 2
done
pct exec "$CTID" -- test -c /dev/net/tun || fail "/dev/net/tun fehlt im Container $CTID."

PORT="$(pct exec "$CTID" -- sed -n 's/^FILOMAT_PORT=//p' /opt/fil-o-mat/backend/.env 2>/dev/null || true)"
PORT="${PORT:-8080}"

info "Tailscale installieren und anmelden"
pct exec "$CTID" -- env \
  TS_AUTHKEY="$TS_AUTHKEY" TS_HOSTNAME="$TS_HOSTNAME" TS_SERVE="$TS_SERVE" PORT="$PORT" \
  bash -euo pipefail -c '
    export DEBIAN_FRONTEND=noninteractive
    if ! command -v tailscale >/dev/null; then
      apt-get update -qq
      apt-get install -y -qq curl jq >/dev/null
      curl -fsSL https://tailscale.com/install.sh | sh >/dev/null
    fi
    command -v jq >/dev/null || apt-get install -y -qq jq >/dev/null
    systemctl enable --now tailscaled >/dev/null

    # Kein --accept-routes: die Drucker im LAN sollen direkt über eth0 erreichbar bleiben,
    # auch wenn ein Subnet-Router im Tailnet dasselbe Netz anbietet.
    args=(--hostname "$TS_HOSTNAME")
    [[ -n "$TS_AUTHKEY" ]] && args+=(--authkey "$TS_AUTHKEY")
    if [[ "$(tailscale status --json | jq -r .BackendState)" != Running ]]; then
      [[ -z "$TS_AUTHKEY" ]] && echo "Den folgenden Link öffnen und den Container im Tailnet anmelden:"
      tailscale up "${args[@]}"
    fi

    if [[ "$TS_SERVE" == 1 ]]; then
      tailscale serve --bg "http://127.0.0.1:$PORT" >/dev/null
    fi
  ' || fail "Tailscale ließ sich nicht einrichten."

TS_IP="$(pct exec "$CTID" -- tailscale ip -4 | head -n1)"
TS_NAME="$(pct exec "$CTID" -- bash -c 'tailscale status --json | jq -r ".Self.DNSName" | sed "s/\.$//"')"

info "Tailscale läuft"
echo "  Backend über Tailscale:  http://${TS_NAME}:${PORT}"
echo "                           http://${TS_IP}:${PORT}"
[[ "$TS_SERVE" == 1 ]] && echo "  mit HTTPS:              https://${TS_NAME}"
echo
echo "  Eine dieser Adressen in der Android- und iOS-App als Backend eintragen."
echo "  Auf dem Handy muss dafür die Tailscale-App laufen."
