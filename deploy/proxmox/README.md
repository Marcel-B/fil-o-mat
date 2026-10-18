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
