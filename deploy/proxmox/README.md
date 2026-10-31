# Backend auf Proxmox (LXC)

`create-lxc.sh` legt auf dem Proxmox-Host einen unprivilegierten Debian-12-Container an, installiert Docker und startet darin das Backend.

```bash
# in der Proxmox-Shell als root
bash create-lxc.sh
```

Einstellungen über Umgebungsvariablen:

| Variable | Standard | Bedeutung |
|---|---|---|
| `CTID` | nächste freie ID | Container-ID |
| `CT_HOSTNAME` | `filomat` | Hostname |
| `STORAGE` | `local-lvm` | Speicher für die Disk |
| `TEMPLATE_STORAGE` | `local` | Speicher für die Debian-Vorlage |
| `BRIDGE` | `vmbr0` | Netzwerk-Bridge |
| `IP` / `GATEWAY` | `dhcp` | z. B. `IP=192.168.1.50/24 GATEWAY=192.168.1.1` |
| `DISK_GB` / `MEMORY_MB` / `CORES` | `8` / `1024` / `2` | Ressourcen |
| `BRANCH` | `main` | Branch, der gebaut wird |
| `GITHUB_TOKEN` | – | nur bei privatem Repository (Token mit Lesezugriff) |
| `FILOMAT_API_KEY` | – | optionaler API-Schlüssel für die Apps |
| `PORT` | `8080` | Port des Backends |
| `NEXTCLOUD_WEBDAV_URL`, `NEXTCLOUD_USERNAME`, `NEXTCLOUD_APP_PASSWORD` | – | optionale nächtliche Sicherung in die Nextcloud |
| `TAILSCALE` | `0` | `1`: Backend zusätzlich über Tailscale erreichbar machen |
| `TS_AUTHKEY` | – | Auth-Key fürs Tailnet; ohne zeigt Tailscale einen Login-Link an |
| `TS_SERVE` | `0` | `1`: zusätzlich HTTPS über `tailscale serve` |

## Tailscale

Damit die Apps das Backend auch unterwegs erreichen, läuft Tailscale direkt im LXC (kein Sidecar-Container). Ein neuer Container bekommt es mit `TAILSCALE=1 bash create-lxc.sh`, ein bestehender so:

```bash
# in der Proxmox-Shell als root
CTID=<CTID> bash add-tailscale.sh
```

Das Skript reicht `/dev/net/tun` an den Container durch (zwei `lxc.*`-Zeilen in `/etc/pve/lxc/<CTID>.conf`, startet ihn dafür einmal neu), installiert Tailscale und meldet ihn im Tailnet an: mit `TS_AUTHKEY` ohne Rückfrage, sonst über den angezeigten Link. Mehrmals aufrufen schadet nicht. Am Ende stehen die Adressen da, die in der App als Backend eingetragen werden, z. B. `http://filomat.<tailnet>.ts.net:8080` oder `http://100.x.y.z:8080`. Auf dem Handy muss die Tailscale-App verbunden sein; im LAN funktioniert die alte Adresse weiter.

Mit `TS_SERVE=1` gibt es zusätzlich `https://filomat.<tailnet>.ts.net` ohne Port (dafür im Tailnet MagicDNS und HTTPS-Zertifikate einschalten).

Die Drucker im LAN erreicht das Backend weiter direkt über `eth0`: Tailscale wird ohne `--accept-routes` gestartet, ein Subnet-Router im Tailnet lenkt den Verkehr also nicht um.

## Aktualisieren

Im LXC prüft ein systemd-Timer alle 15 Minuten, ob es auf dem gebauten Branch neue Commits gibt. Die iOS-App zeigt sie unter **Backend** an; nach der Bestätigung holt der Updater die Commits, baut das Image neu und startet das Backend neu. Lässt sich das neue Image nicht bauen, läuft die bisherige Version weiter.

Ein LXC, der vor dem Updater angelegt wurde, bekommt ihn so:

```bash
pct exec <CTID> -- bash -c 'cd /opt/fil-o-mat && git pull && bash deploy/updater/install.sh && cd backend && docker compose up -d --build'
```

Von Hand:

```bash
pct exec <CTID> -- /opt/fil-o-mat/deploy/updater/filomat-update.sh check   # nur prüfen
pct exec <CTID> -- /opt/fil-o-mat/deploy/updater/filomat-update.sh apply   # aktualisieren
pct exec <CTID> -- journalctl -u filomat-update -n 50                      # Log des letzten Updates
```

So funktioniert es: `filomat-update.sh` schreibt den Stand nach `/var/lib/filomat-update/status.json`. Dieser Ordner ist im Container unter `/update` eingebunden (`backend/docker-compose.updater.yml`, eingetragen über `COMPOSE_FILE` in `backend/.env`). Die API liest die Datei (`GET /api/update`) und legt für einen Auftrag `request` an (`POST /api/update`, `POST /api/update/check`); darauf wartet die systemd-Unit `filomat-update.path`. Der Container selbst bekommt so keinen Zugriff auf Docker.

Die Datenbank liegt im Docker-Volume `backend_filomat-data` im Container.

## Überwachung einschalten

```bash
# in der Proxmox-Shell als root, im Ordner mit diesem Skript (oder es vorher aus dem Repository holen)
bash monitoring-einrichten.sh
```

Holt den neuesten Stand, schaltet `/metrics` und den Versand an Loki in `backend/.env` ein (`MONITORING_METRICS`, `MONITORING_LOKI_URL`, `MONITORING_LOKI_HOST`), baut neu und prüft beides. Am Ende steht der Aufruf, der das Ziel beim Monitoring-Stack (Tonwerk-AI, `deploy/monitoring/`) einträgt. Loki ist nur über das Tailnet erreichbar, der Container braucht also Tailscale (siehe oben). Die Messwerte stehen in [backend/README.md](../../backend/README.md#überwachung).

## Nextcloud-Sicherung nachträglich einschalten

```bash
pct exec <CTID> -- bash -c 'cd /opt/fil-o-mat/backend && cat >> .env <<ENV
NEXTCLOUD_WEBDAV_URL=https://cloud.example.de/remote.php/dav/files/<benutzer>/Backups/fil-o-mat
NEXTCLOUD_USERNAME=<benutzer>
NEXTCLOUD_APP_PASSWORD=<app-passwort>
ENV
docker compose up -d --build && sleep 5 && curl -s -X POST localhost:8080/api/backup'
```

Mit API-Schlüssel braucht der `curl` noch `-H "X-Api-Key: …"`. Details und Zurückspielen: [backend/README.md](../../backend/README.md#sicherung-in-die-nextcloud).
