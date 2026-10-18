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
| PUT | `/api/printers/{id}` | Name, Adresse, Schlüssel, aktiv/inaktiv |
| DELETE | `/api/printers/{id}` | Drucker und Container entfernen; Spulen wandern nach „Lose“ |
| POST | `/api/printers/{id}/sync` | Sofort abgleichen |
| GET | `/api/consumption?itemId=&printerId=&unassigned=true` | Verbrauchsbuchungen |
| POST | `/api/consumption/{id}/assign` | Offene Buchung zuordnen (`{"itemId": "…", "spoolId": "…"}`, Spule optional) und abziehen |
| GET | `/api/spools?itemId=&printerId=&includeEmpty=true` | Angebrochene Spulen |
| POST | `/api/items/{id}/spools` | Neue Spule anbrechen (`{"remainingWeight", "printerId", "tool"}`, alles optional), Vorrat minus eins |
| PUT | `/api/spools/{id}` | Restgewicht und Platz setzen (`printerId: null` = Lager) |
| POST | `/api/spools/{id}/empty` | Spule ist leer |
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

Der U1 erkennt die NFC-Tags (OpenSpool) in jedem seiner vier Köpfe selbst. Das Backend bucht daher jeden Kopf einzeln auf die Spule mit diesem Tag, verschiebt erkannte Spulen automatisch in den Drucker-Container und verwendet die Spulen, die beim vorigen Abgleich eingelegt waren (also während des Drucks). Grundlage sind die Slicer-Angaben pro Filament (`filament_weight`, z. B. OrcaSlicer), bei abgebrochenen Drucken anteilig.

PrusaLink hat keine Druckhistorie. Das Backend merkt sich deshalb bei jedem Abgleich den laufenden Druck und bucht ihn, sobald er beendet ist oder der Drucker keinen Druck mehr meldet (abgebrochene Drucke anteilig nach Fortschritt). Ist ein Druck schon vom Display verschwunden, bevor der nächste Abgleich (alle zwei Minuten) ihn als beendet sieht, wird nach dem zuletzt gesehenen Fortschritt gebucht; ab 99,5 % gilt er als fertig. Mit MMU3 kommen die Slicer-Werte pro Slot; gebucht wird jeweils auf die Spule, die in der App an diesem Slot eingelegt ist.

Weitere Typen (Bambu LAN, Anycubic) folgen.

### Tags auf Musterplättchen

Ein Filament (Item) ist eine Sorte: Das Tag klebt z. B. auf einem Musterplättchen und bleibt, wenn eine Spule leer ist. `spoolsInStock` zählt die vollen Spulen im Vorrat.

Jede angebrochene Spule (`Spool`) hat eine Nummer pro Sorte (mit Stift auf die Spule schreiben), ein eigenes Restgewicht und einen Platz: Drucker und Kopf (`printerId`, `tool`) oder Lager. So können mehrere Spulen derselben Sorte gleichzeitig laufen, etwa als Nachfüllspule am U1 und in einem anderen Drucker.

Gebucht wird auf die Spule, die an diesem Drucker-Kopf steckt. Steckt dort keine Spule der Sorte, wird die einzige Lager-Spule der Sorte eingelegt, sonst eine neue angebrochen. Reicht eine Spule für einen Druck nicht, wird sie als leer markiert und am selben Platz eine aus dem Vorrat angebrochen; ohne Vorrat bleibt das Restgewicht bei 0. Wird eine Spule an einen belegten Platz gelegt, wandert die bisherige ins Lager.

## Datenbank-Migrationen

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Filomat.Api -o Data/Migrations
```

Migrationen werden beim Start automatisch angewendet.
