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

Aktualisieren:

```bash
pct exec <CTID> -- bash -c 'cd /opt/fil-o-mat && git pull && cd backend && docker compose up -d --build'
```

Die Datenbank liegt im Docker-Volume `backend_filomat-data` im Container.

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
