# Fil-o-mat – Backend

ASP.NET Core (.NET 10) mit SQLite. Gemeinsame Datenbasis für die Android- und die iOS-App und die Anbindung der Drucker für das Verbrauchs-Tracking.

## Starten

Mit Docker (z. B. auf NAS oder Raspberry Pi):

```bash
cd backend
docker compose up -d --build
```

Die API läuft dann auf Port 8080, die Datenbank liegt im Volume `filomat-data`.

Lokal zum Entwickeln:

```bash
cd backend
dotnet run --project src/Filomat.Api
dotnet test
```

## Konfiguration

| Einstellung | Umgebungsvariable | Bedeutung |
|---|---|---|
| `ConnectionStrings:Filomat` | `ConnectionStrings__Filomat` | SQLite-Datei, Standard `filomat.db` (im Container `/data/filomat.db`) |
| `Filomat:ApiKey` | `Filomat__ApiKey` | Optionaler Schlüssel; wenn gesetzt, muss jeder `/api`-Aufruf den Header `X-Api-Key` mitschicken |
| `Filomat:PrinterSyncInterval` | `Filomat__PrinterSyncInterval` | Abstand des Drucker-Abgleichs, Standard `00:02:00`; `00:00:00` schaltet ihn ab |
| – | `NEXTCLOUD_WEBDAV_URL`, `NEXTCLOUD_USERNAME`, `NEXTCLOUD_APP_PASSWORD` | Nextcloud-Sicherung, siehe unten |
| `Ntfy:Url`, `Ntfy:Token` | `Ntfy__Url`, `Ntfy__Token` (in `backend/.env`: `NTFY_URL`, `NTFY_TOKEN`) | Meldungen über ntfy, falls in der App keine Adresse eingestellt ist; siehe unten |
| `Backup:At` | `Backup__At` | Uhrzeit der nächtlichen Sicherung, Standard `03:00` (`Backup:TimeZone`, Standard `Europe/Berlin`); leer für nur manuelle |

## API

Die OpenAPI-Beschreibung liegt unter `/openapi/v1.json`.

| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/api/containers` | Alle Container |
| GET | `/api/containers/{id}` · `/api/containers/by-tag/{rfidTag}` | Ein Container |
| GET | `/api/containers/{id}/items` | Filamente in einem Container |
| POST | `/api/containers` | Container anlegen (ID optional, z. B. von der App vergeben) |
| PUT | `/api/containers/{id}` | Name, Beschreibung, Menge ändern |
| DELETE | `/api/containers/{id}` | Löschen; Filamente wandern nach „Lose“, Standard-Container bleiben |
| GET | `/api/items?containerId=` | Filamente, optional pro Container |
| GET | `/api/items/{id}` · `/api/items/by-tag/{rfidTag}` | Ein Filament |
| POST | `/api/items` | Filament anlegen |
| PUT | `/api/items/{id}` | Name, Beschreibung, Tag-Daten, Gewichte ändern |
| POST | `/api/items/{id}/move` | Einlagern (`{"containerId": "…"}`) oder herausnehmen (`{"containerId": null}`) |
| DELETE | `/api/items/{id}` | Filament löschen |
| GET | `/api/printers` · `/api/printers/{id}` | Drucker mit Stand des letzten Abgleichs |
| POST | `/api/printers` | Drucker anlegen, legt seinen Container mit an |
| PUT | `/api/printers/{id}` | Name, Adresse, Schlüssel, aktiv/inaktiv; neue Adresse oder neuer Schlüssel gleicht sofort ab |
| DELETE | `/api/printers/{id}` | Drucker und Container entfernen; Spulen wandern nach „Lose“ |
| POST | `/api/printers/{id}/sync` | Sofort abgleichen |
| GET | `/api/printers/status` · `/api/printers/{id}/status` | Zustand (Druck, Fortschritt, Restzeit, Temperaturen, Slots, Feuchte), Spulen pro Kopf mit „reicht/reicht nicht“ und Hinweise; älter als 10 s wird neu abgefragt |
| GET | `/api/printers/{id}/thumbnail` | Vorschaubild des laufenden Drucks (404, wenn es keins gibt) |
| GET | `/api/stats?months=12` | Verbrauch, Drucke, Abbrüche und Druckzeit pro Monat, Drucker, Sorte und Material |
| GET · PUT | `/api/settings/notifications` | ntfy-Adresse für Meldungen (`{"url", "token"}`; leere Adresse = Server-Konfiguration, `token: null` behält ihn) |
| POST | `/api/settings/notifications/test` | Testmeldung schicken |
| GET | `/api/consumption?itemId=&printerId=&unassigned=true` | Verbrauchsbuchungen |
| POST | `/api/consumption/{id}/assign` | Offene Buchung zuordnen (`{"itemId": "…", "spoolId": "…"}`, Spule optional) und abziehen |
| GET | `/api/spools?itemId=&printerId=&includeEmpty=true` | Angebrochene Spulen |
| POST | `/api/items/{id}/spools` | Neue Spule anbrechen (`{"remainingWeight", "printerId", "tool", "containerId"}`, alles optional), Vorrat minus eins |
| PUT | `/api/spools/{id}` | Restgewicht und Platz setzen (`printerId: null` = Lager, dazu optional `containerId`) |
| POST | `/api/spools/{id}/empty` | Spule ist leer |
| GET | `/api/backup` | Stand der Nextcloud-Sicherung: eingerichtet, letzte, nächste |
| POST | `/api/backup` | Sofort sichern |
| GET | `/health` | Lebenszeichen (ohne API-Schlüssel) |

Tags sind eindeutig: ein Tag gehört entweder zu genau einem Container oder zu genau einem Filament. Die Tag-Daten (`itemData`) verwenden dasselbe OpenSpool-Format wie auf dem NFC-Tag (`color_hex`, `min_temp` …).

## Drucker und Verbrauch

Jeder Drucker bekommt einen eigenen Container. Die Spule, die gerade im Drucker steckt, wird in diesen Container eingelagert (per App wie bei jedem anderen Container; mit `rfidTag` beim Anlegen kann der Drucker einen echten NFC-Tag bekommen).

Der Hintergrund-Abgleich holt beendete Drucke vom Drucker und legt pro Druck eine Buchung an. Liegt genau eine Sorte im Drucker-Container, wird der Verbrauch von der angebrochenen Spule dieser Sorte im Drucker abgezogen (siehe unten). Bei keiner oder mehreren Spulen bleibt die Buchung offen und kann später zugeordnet werden. Drucke von vor dem Anlegen des Druckers werden ignoriert, jeder Druck wird nur einmal gebucht.

Gramm werden aus dem Verhältnis von Slicer-Gewicht zu Slicer-Länge berechnet (stimmt auch bei abgebrochenen Drucken), ohne Slicer-Angaben über die Dichte des Filamenttyps bei 1,75 mm.

| Typ | Drucker | Adresse |
|---|---|---|
| `Moonraker` | Klipper, z. B. Voron | `http://<drucker>:7125` (oder über Fluidd/Mainsail-Port); `apiKey` nur bei aktivierter Moonraker-Authentifizierung |
| `SnapmakerU1` | Snapmaker U1 mit paxx12-Firmware | `http://<drucker>` |
| `PrusaLink` | Prusa MK4, Core One, Mini, XL | `http://<drucker>`; `apiKey` aus Einstellungen > Netzwerk > PrusaLink am Drucker |
| `Anycubic` | Anycubic Kobra X, S1, 3, 4 im LAN-Modus | `http://<drucker>` (Port 18910 wird ergänzt); kein Schlüssel |
| `Bambu` | Bambu Lab P1S, P1P, X1, A1 | `http://<drucker>`; `apiKey` = Zugangscode (Einstellungen > WLAN am Drucker), optional `Seriennummer:Zugangscode` |

Der U1 erkennt die NFC-Tags (OpenSpool) in jedem seiner vier Köpfe selbst. Das Backend bucht daher jeden Kopf einzeln auf die Spule mit diesem Tag, verschiebt erkannte Spulen automatisch in den Drucker-Container und verwendet die Spulen, die beim vorigen Abgleich eingelegt waren (also während des Drucks). Grundlage sind die Slicer-Angaben pro Filament (`filament_weight`, z. B. OrcaSlicer), bei abgebrochenen Drucken anteilig.

PrusaLink hat keine Druckhistorie. Das Backend merkt sich deshalb bei jedem Abgleich den laufenden Druck und bucht ihn, sobald er beendet ist oder der Drucker keinen Druck mehr meldet (abgebrochene Drucke anteilig nach Fortschritt). Ist ein Druck schon vom Display verschwunden, bevor der nächste Abgleich (alle zwei Minuten) ihn als beendet sieht, wird nach dem zuletzt gesehenen Fortschritt gebucht; ab 99,5 % gilt er als fertig. Mit MMU3 kommen die Slicer-Werte pro Slot; gebucht wird jeweils auf die Spule, die in der App an diesem Slot eingelegt ist.

Anycubic-Drucker werden im LAN-Modus abgefragt (am Drucker Einstellungen > Netzwerk > LAN-Modus; die Anycubic-Cloud und die App sind dann aus, Anycubic Slicer Next druckt weiter im LAN). Der Drucker gibt über `:18910/info` und eine signierte Anfrage Zugangsdaten für seinen MQTT-Broker (TLS, Port 9883) heraus, die bei jedem Abgleich neu geholt werden; Ablauf nach [anycubic-lan](https://github.com/Nino6689/anycubic-lan/blob/main/docs/PROTOCOL.md). Eine Historie gibt es nicht, aber der laufende Druck meldet die tatsächlich geförderte Länge (`supplies_usage`, mm, inklusive Spülen). Das Backend verfolgt den Druck und rechnet jeden Zuwachs zwischen zwei Abgleichen dem ACE-Slot zu, der beim vorigen Abgleich förderte (Slots über alle ACE-Einheiten gezählt, Einheit · 4 + Slot). Bei Mehrfarbdrucken ist die Aufteilung deshalb nur so genau wie das Abgleich-Intervall. Gramm über die Dichte des Materials, das die ACE für den Slot meldet. Gebucht wird auf die Spule, die in der App an diesem Slot eingelegt ist; ohne ACE auf die Spule im Drucker.

Der Kobra X meldet seinen eingebauten Vierer als Einheit -1. Dann zählen die Slots ab ihm: eingebaut 0–3, erste externe ACE 4–7 (in der App Slot 1–4 und 5–8). Der eingebaute Slot, der zur externen ACE durchreicht (`edit_status` 3), fördert nie selbst. Welcher Slot gerade fördert, liest der Adapter aus `loaded_slot`; der Kobra X hat mehrere Slots zugleich im Kopf eingezogen, daher gilt Status 5 nur, wenn es genau ein Slot ist. `last_project` des Kobra X hat kein `print_status`, nur `state` (`finished`).

Bambu-Drucker werden über ihren MQTT-Broker im LAN abgefragt (TLS, Port 8883, Benutzer `bblp`, Passwort = Zugangscode, Thema `device/<seriennummer>/report`). Die Seriennummer steht im Zertifikat des Druckers, das Backend liest sie dort; nur falls das nicht klappt, gehört sie mit Doppelpunkt vor den Zugangscode. Die P1-Serie schickt nur Änderungen, daher fragt das Backend bei jedem Abgleich mit `pushall` den ganzen Zustand ab und trennt wieder. Eine Historie und die geförderte Länge kennt der Drucker nicht. Das Backend verfolgt deshalb den laufenden Druck (`gcode_state`, `mc_percent`) und holt bei dessen Beginn die Druckdatei per FTPS von der SD-Karte (implizites TLS, Port 990, gleiche Zugangsdaten; im LAN gesendete Drucke liegen als `<name>.gcode.3mf` im Wurzelverzeichnis, Cloud-Drucke in `/cache`). Aus `Metadata/slice_info.config` kommen Gramm und Meter pro Filament der gedruckten Platte. Bei jedem Abgleich notiert der Adapter, welches AMS-Fach gerade fördert (`tray_now`). Am Ende gehört jedes Filament zum Fach mit derselben Farbe, bevorzugt unter den Fächern, die gefördert haben; ein einfarbiger Druck zu dem Fach, das förderte (beim Nachfüllen aus einem zweiten Fach anteilig). Abgebrochene Drucke (`FAILED`) werden anteilig nach Fortschritt gebucht. Fehlt die Druckdatei, bleibt die Restmenge, die das AMS für Bambu-Spulen mit Chip meldet (`remain` in Prozent des Nenngewichts); sonst wird der Druck mit 0 g gebucht.

Slots: AMS-Einheit · 4 + Fach (Slot 1–4 erstes AMS), danach die externe Spule als letzter Slot (mit einem AMS Slot 5); ohne AMS ein Slot. AMS HT (ab ID 128) wird nicht berücksichtigt. Gebucht wird auf die Spule, die in der App an diesem Slot eingelegt ist. Neuere Firmware lässt Fremdprogramme womöglich nur im LAN-Modus mit eingeschaltetem Entwicklermodus zu (Einstellungen > WLAN am Drucker); zeigt der Drucker in der App einen MQTT-Fehler, dort nachsehen.

### Zustand und Hinweise

Nach jedem Abgleich und auf `GET /api/printers/status` fragt das Backend den Zustand ab, Anycubic und Bambu direkt aus der Meldung des Abgleichs. Moonraker liefert ihn über `printer/objects/query` (`print_stats`, `virtual_sdcard`, Heizungen, beim U1 dazu `filament_detect`) und die Slicer-Angaben der Datei über `server/files/metadata`, PrusaLink über `/api/v1/status` und `/api/v1/job`, Bambu aus dem `pushall`-Bericht, Anycubic aus `info` und der ACE-Meldung. Der Zustand liegt nur im Speicher.

Daraus entstehen Hinweise:

- **Spule reicht nicht:** Slicer-Gramm pro Kopf mal fehlender Fortschritt gegen das Restgewicht der Spule. Der Kobra kennt das Slicer-Gewicht nicht; dort wird ab 3 % aus der bisher geförderten Länge hochgerechnet.
- **Tag oder Slot passt nicht:** U1 erkennt ein anderes Tag; ACE oder AMS melden ein anderes Material (Grundmaterial, „PLA+“ = „PLA“) oder eine deutlich andere Farbe; Slot leer, aber in der App belegt, oder umgekehrt.
- **Temperatur:** Die Düse heizt mehr als 5 °C außerhalb von `min_temp`–`max_temp` der Sorte, die gerade druckt.
- **Feuchte:** ACE über 40 % bei PETG, TPU, PA, PC und anderen empfindlichen Materialien, sonst über 55 %; Bambu-AMS bei Stufe 1 oder 2 von 5.

### Meldungen über ntfy

Mit einer ntfy-Adresse (in der App unter Drucker > Meldungen oder per `Ntfy__Url`) meldet das Backend beendete und abgebrochene Drucke mit Verbrauch und Restgewicht pro Spule („fast leer“ unter 10 % bzw. 50 g), Fehler des Druckers und einmal pro Druck, wenn eine Spule nicht reicht. Ein Thema auf [ntfy.sh](https://ntfy.sh) reicht (schwer erratbarer Name, z. B. `https://ntfy.sh/filomat-7f3k2q`), in der ntfy-App abonnieren; das klappt auch auf GrapheneOS ohne Google-Dienste. Für einen eigenen ntfy-Server mit Zugriffsschutz kommt der Token (`tk_…`) dazu.

### Tags auf Musterplättchen

Ein Filament (Item) ist eine Sorte: Das Tag klebt z. B. auf einem Musterplättchen und bleibt, wenn eine Spule leer ist. `spoolsInStock` zählt die vollen Spulen im Vorrat.

Jede angebrochene Spule (`Spool`) hat eine Nummer pro Sorte (mit Stift auf die Spule schreiben), ein eigenes Restgewicht und einen Platz: Drucker und Kopf (`printerId`, `tool`) oder Lager. So können mehrere Spulen derselben Sorte gleichzeitig laufen, etwa als Nachfüllspule am U1 und in einem anderen Drucker.

Gebucht wird auf die Spule, die an diesem Drucker-Kopf steckt. Steckt dort keine Spule der Sorte, wird die einzige Lager-Spule der Sorte eingelegt, sonst eine neue angebrochen. Reicht eine Spule für einen Druck nicht, wird sie als leer markiert und am selben Platz eine aus dem Vorrat angebrochen; ohne Vorrat bleibt das Restgewicht bei 0. Wird eine Spule an einen belegten Platz gelegt, wandert die bisherige ins Lager.

## Sicherung in die Nextcloud

Jede Nacht um 3 Uhr (und auf `POST /api/backup`) kopiert das Backend die Datenbank über SQLites Online-Backup, packt sie als ZIP und lädt sie per WebDAV in einen Nextcloud-Ordner, wie in YuE UI. Es entstehen `filomat-mon.zip` … `filomat-sun.zip` (die letzten sieben Tage) und `filomat-2026-10.zip` (der letzte Stand jedes Monats); in der Nextcloud wird nichts gelöscht. Wie die letzte Sicherung lief, steht unter `GET /api/backup` und in `backup.json` neben der Datenbank. Schlägt die nächtliche fehl, versucht das Backend es nach 30 Minuten erneut.

Einrichten: in der Nextcloud unter Einstellungen → Sicherheit → Geräte & Sitzungen ein App-Passwort anlegen, dann in `backend/.env`:

```bash
NEXTCLOUD_WEBDAV_URL=https://cloud.example.de/remote.php/dav/files/<benutzer>/Backups/fil-o-mat
NEXTCLOUD_USERNAME=<benutzer>
NEXTCLOUD_APP_PASSWORD=<app-passwort>
```

Der Ordner `fil-o-mat` wird angelegt, der darüber (`Backups`) muss existieren. Danach `docker compose up -d` und zum Prüfen `curl -X POST http://localhost:8080/api/backup` (mit API-Schlüssel `-H "X-Api-Key: …"`), dann `curl http://localhost:8080/api/backup`.

Zurückspielen (im LXC unter `/opt/fil-o-mat/backend`, das entpackte `filomat.db` im aktuellen Ordner; 1654 ist der Benutzer der App im Image):

```bash
docker compose stop
docker run --rm -v backend_filomat-data:/data -v "$PWD":/in alpine sh -c \
  'cp /in/filomat.db /data/ && rm -f /data/filomat.db-wal /data/filomat.db-shm && chown 1654:1654 /data/filomat.db'
docker compose up -d
```

## Datenbank-Migrationen

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Filomat.Api -o Data/Migrations
```

Migrationen werden beim Start automatisch angewendet.
