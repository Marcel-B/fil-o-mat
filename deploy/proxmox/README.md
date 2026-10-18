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

Aktualisieren:

```bash
pct exec <CTID> -- bash -c 'cd /opt/fil-o-mat && git pull && cd backend && docker compose up -d --build'
```

Die Datenbank liegt im Docker-Volume `backend_filomat-data` im Container.
