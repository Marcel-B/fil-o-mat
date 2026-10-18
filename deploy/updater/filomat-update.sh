#!/usr/bin/env bash
# Prüft, ob es auf dem gebauten Branch neue Commits gibt, und spielt sie auf Wunsch ein.
# Läuft auf dem Host des Backends (im LXC) als root, gestartet von systemd (siehe install.sh):
#   filomat-update.sh check     Branch holen und status.json schreiben
#   filomat-update.sh apply     neue Commits übernehmen, Backend neu bauen und starten
#   filomat-update.sh request   Auftrag der API (Datei "request") ausführen
# Die API liest status.json und legt "request" an, siehe backend/src/Filomat.Api/Endpoints/UpdateEndpoints.cs.
set -euo pipefail

# Alles steckt in main, damit bash das Skript ganz gelesen hat, bevor "git merge" es ersetzt
main() {
  REPO="${FILOMAT_REPO:-$(cd "$(dirname "$(readlink -f "$0")")/../.." && pwd)}"
  STATE_DIR="${FILOMAT_UPDATE_DIR:-/var/lib/filomat-update}"
  STATUS="$STATE_DIR/status.json"
  REQUEST="$STATE_DIR/request"
  PORT="$(sed -n 's/^FILOMAT_PORT=//p' "$REPO/backend/.env" 2>/dev/null || true)"
  PORT="${PORT:-8080}"

  mkdir -p "$STATE_DIR"
  exec 9>"$STATE_DIR/lock"
  flock 9

  BRANCH="$(git -C "$REPO" rev-parse --abbrev-ref HEAD)"
  case "${1:-}" in
    check) check ;;
    apply) apply ;;
    request)
      [[ -f "$REQUEST" ]] || exit 0
      local job
      job="$(tr -d '[:space:]' < "$REQUEST")"
      rm -f "$REQUEST"
      if [[ "$job" == apply ]]; then apply; else check; fi
      ;;
    *) echo "Aufruf: $0 check|apply|request" >&2; exit 2 ;;
  esac
}

log() { echo "$*" >&2; }

# Schreibt status.json; $1 = Zustand (idle, running, failed), $2 = Meldung
write_status() {
  local state="$1" message="${2:-}" commits
  commits="$(git -C "$REPO" log --format='%H%x1f%s%x1f%ct' -n 30 "HEAD..origin/$BRANCH" \
    | jq -R -s 'split("\n") | map(select(length > 0) | split("\u001f") | {sha: .[0], subject: .[1], date: (.[2] | tonumber)})')"
  jq -n \
    --arg branch "$BRANCH" \
    --arg current "$(git -C "$REPO" rev-parse HEAD)" \
    --argjson currentDate "$(git -C "$REPO" log -1 --format=%ct HEAD)" \
    --arg latest "$(git -C "$REPO" rev-parse "origin/$BRANCH")" \
    --argjson commits "$commits" \
    --argjson checkedAt "$(date +%s)" \
    --arg state "$state" \
    --arg message "$message" \
    '{branch: $branch, current: $current, currentDate: $currentDate, latest: $latest, commits: $commits,
      checkedAt: $checkedAt, state: $state, message: (if $message == "" then null else $message end)}' \
    > "$STATUS.tmp"
  chmod 644 "$STATUS.tmp"
  mv "$STATUS.tmp" "$STATUS"
}

# Zustand und Meldung des letzten Laufs behalten, damit ein Fehler sichtbar bleibt, bis es wieder klappt
previous() { [[ -f "$STATUS" ]] && jq -r ".$1 // empty" "$STATUS" || true; }

fetch() { git -C "$REPO" fetch --quiet origin "$BRANCH"; }

check() {
  local state message
  state="$(previous state)"
  [[ "$state" == running || -z "$state" ]] && state=idle
  if ! fetch; then
    write_status "$state" "GitHub nicht erreichbar, letzte Prüfung $(date '+%d.%m. %H:%M') ging nicht."
    return 1
  fi
  message="$(previous message)"
  # "GitHub nicht erreichbar" gilt nur bis zur nächsten erfolgreichen Prüfung
  [[ "$message" == "GitHub nicht erreichbar"* ]] && message=""
  write_status "$state" "$message"
}

apply() {
  fetch || { write_status failed "GitHub nicht erreichbar."; return 1; }
  local old
  old="$(git -C "$REPO" rev-parse HEAD)"
  if [[ "$old" == "$(git -C "$REPO" rev-parse "origin/$BRANCH")" ]]; then
    write_status idle ""
    return 0
  fi

  write_status running "Aktualisierung läuft."
  log "Aktualisiere $BRANCH von ${old:0:7} auf $(git -C "$REPO" rev-parse --short "origin/$BRANCH")"
  if ! git -C "$REPO" merge --ff-only --quiet "origin/$BRANCH"; then
    write_status failed "git merge ging nicht (lokale Änderungen im Checkout?)."
    return 1
  fi

  # systemd-Units können sich mit dem neuen Stand geändert haben
  "$REPO/deploy/updater/install.sh" --units-only || log "Units nicht erneuert"

  if ! (cd "$REPO/backend" && docker compose build --quiet); then
    # Das alte Image läuft weiter; den Checkout zurücksetzen, damit Stand und laufende Version passen
    git -C "$REPO" reset --quiet --hard "$old"
    write_status failed "Neues Image ließ sich nicht bauen, die bisherige Version läuft weiter."
    return 1
  fi
  (cd "$REPO/backend" && docker compose up -d --quiet-pull)

  for _ in $(seq 1 60); do
    if curl -fs "http://localhost:$PORT/health" >/dev/null; then
      docker image prune -f >/dev/null || true
      write_status idle ""
      log "Fertig"
      return 0
    fi
    sleep 2
  done
  write_status failed "Das Backend antwortet nach dem Update nicht (docker compose logs)."
  return 1
}

main "$@"
