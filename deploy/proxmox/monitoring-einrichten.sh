#!/usr/bin/env bash
# Meldet Fil-o-mat beim Monitoring-Stack an (Tonwerk-AI, deploy/monitoring/): holt den neuesten Stand von main, schaltet
# in backend/.env /metrics und den Versand an Loki ein (andere Zeilen bleiben), baut und startet das Backend neu, prüft
# /metrics und Loki und nennt den Aufruf, der das Ziel in Prometheus einträgt. Idempotent.
#
# In der Proxmox-Shell als root:
#   bash monitoring-einrichten.sh
# Optional: CTID (sonst der Container mit dem Namen filomat), LOKI_URL (http://monitoring:3100), LOKI_HOST (proxmox).
set -euo pipefail

CTID="${CTID:-$(pct list | awk '$NF == "filomat" { print $1; exit }')}"
LOKI_URL="${LOKI_URL:-http://monitoring:3100}"
LOKI_HOST="${LOKI_HOST:-proxmox}"

info() { echo -e "\e[1;34m==>\e[0m $*"; }
fail() { echo -e "\e[1;31mFehler:\e[0m $*" >&2; exit 1; }

command -v pct >/dev/null || fail "Das Skript muss auf dem Proxmox-Host laufen (pct fehlt)."
[[ -n "$CTID" ]] || fail "Kein Container namens filomat; CTID=<Nummer> angeben."
pct status "$CTID" | grep -q running || fail "Container $CTID läuft nicht."

info "Container $CTID: neuester Stand, Monitoring in backend/.env, neu bauen"
pct exec "$CTID" -- env LOKI_URL="$LOKI_URL" LOKI_HOST="$LOKI_HOST" bash -euo pipefail -c '
  cd /opt/fil-o-mat
  git fetch --quiet origin main
  git merge --ff-only --quiet origin/main || { echo "git merge ging nicht (lokale Änderungen im Checkout?)" >&2; exit 1; }
  git log -1 --oneline
  cd backend
  touch .env
  # Nur diese drei Zeilen ersetzen, der Rest (API-Schlüssel, Nextcloud, ntfy, COMPOSE_FILE) bleibt
  sed -i "/^MONITORING_\(METRICS\|LOKI_URL\|LOKI_HOST\)=/d" .env
  printf "MONITORING_METRICS=true\nMONITORING_LOKI_URL=%s\nMONITORING_LOKI_HOST=%s\n" "$LOKI_URL" "$LOKI_HOST" >> .env
  chmod 600 .env
  docker compose up -d --build --quiet-pull 2>&1 | tail -n 3
  port="$(sed -n "s/^FILOMAT_PORT=//p" .env | tail -n 1)"
  port="${port:-8080}"
  for _ in $(seq 1 30); do
    if curl -fs "http://localhost:$port/metrics" | grep -q "^filomat_items "; then
      curl -fs "http://localhost:$port/metrics" | grep -E "^filomat_(items|containers) " | sed "s/^/    /"
      break
    fi
    sleep 2
  done
  curl -fs "http://localhost:$port/metrics" | grep -q "^filomat_items " || { docker compose logs --tail 30; echo "/metrics liefert keine Messwerte" >&2; exit 1; }
  docker compose logs --tail 200 | grep "Monitoring:" | tail -n 1 | sed "s/^/    /" || true
  if curl -fs --max-time 5 "${LOKI_URL%/}/ready" >/dev/null; then
    echo "    $LOKI_URL antwortet."
  else
    echo "Warnung: $LOKI_URL antwortet nicht. Läuft Tailscale im Container (add-tailscale.sh)?" >&2
  fi
' || fail "Einrichten im Container ging nicht, Ausgabe steht oben."

name="$(pct exec "$CTID" -- sh -c 'tailscale status --json 2>/dev/null' | sed -n 's/.*"DNSName": *"\([^"]*\)\.".*/\1/p' | head -n 1 || true)"
port="$(pct exec "$CTID" -- sh -c 'sed -n "s/^FILOMAT_PORT=//p" /opt/fil-o-mat/backend/.env | tail -n 1')"
info "Fertig. Jetzt hier das Ziel in Prometheus eintragen (holt den Stack frisch, samt Dashboard „fil-o-mat“):"
cat <<NEXT

  curl -fsSL https://codeload.github.com/Marcel-B/Tonwerk-AI/tar.gz/main | tar -xz --strip-components=2 -C /root Tonwerk-AI-main/deploy/monitoring
  APP_TARGETS="filomat=${name:-<Tailscale-Name des Containers>}:${port:-8080}" /root/monitoring/proxmox-lxc.sh

NEXT
