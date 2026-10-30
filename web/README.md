# Fil-o-mat – Web-Oberfläche

Vue 3, PrimeVue 4, Tailwind CSS 4 und Pinia. Läuft im selben Docker-Image wie das Backend und ist unter dessen Adresse erreichbar (z. B. `http://<lxc>:8080/`).

Kann dasselbe wie die Apps, außer NFC: Drucker mit Zustand, Spulen pro Kopf und Hinweisen (alle 15 s neu), Spule einlegen oder wiegen, Filamente (Sorten) anlegen, bearbeiten und zusammenführen, Spulen anbrechen, Container, offene Verbrauchsbuchungen zuordnen, Statistik, ntfy, Sicherung und Update. Tags (Musterplättchen) kommen weiter per App dazu; Container ohne Tag bekommen ein Platzhalter-Tag `WEB_…`.

Ist im Backend `FILOMAT_API_KEY` gesetzt, unter Einstellungen den Schlüssel eingeben; er bleibt im Browser gespeichert.

## Entwickeln

```bash
cd web
npm install
npm run dev                                       # http://localhost:5173, /api geht an http://localhost:8080
FILOMAT_BACKEND=http://192.168.2.50:8080 npm run dev   # gegen das Backend im LXC
npm run build                                     # Typprüfung und Bau nach dist/
```

Im Image baut `backend/Dockerfile` die Oberfläche und legt sie nach `wwwroot`; das Backend liefert sie aus und gibt für alle Pfade außerhalb von `/api` die `index.html` zurück. Deshalb baut `docker compose` aus dem Wurzelordner des Repos (`context: ..` in `backend/docker-compose.yml`).

`src/presets/*.json` sind dieselben Dateien wie in `ios-app/Filomat/Resources`.
