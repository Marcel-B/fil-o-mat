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
| POST | `/api/consumption/{id}/assign` | Offene Buchung einer Spule zuordnen (`{"itemId": "…"}`) und abziehen |
| GET | `/health` | Lebenszeichen (ohne API-Schlüssel) |

Tags sind eindeutig: ein Tag gehört entweder zu genau einem Container oder zu genau einem Filament. Die Tag-Daten (`itemData`) verwenden dasselbe OpenSpool-Format wie auf dem NFC-Tag (`color_hex`, `min_temp` …).

## Drucker und Verbrauch

Jeder Drucker bekommt einen eigenen Container. Die Spule, die gerade im Drucker steckt, wird in diesen Container eingelagert (per App wie bei jedem anderen Container; mit `rfidTag` beim Anlegen kann der Drucker einen echten NFC-Tag bekommen).

Der Hintergrund-Abgleich holt beendete Drucke vom Drucker und legt pro Druck eine Buchung an. Liegt genau eine Spule im Drucker-Container, wird der Verbrauch von ihrem Restgewicht (`remainingWeight`, sonst `netWeight`) abgezogen. Bei keiner oder mehreren Spulen bleibt die Buchung offen und kann später zugeordnet werden. Drucke von vor dem Anlegen des Druckers werden ignoriert, jeder Druck wird nur einmal gebucht.

Gramm werden aus dem Verhältnis von Slicer-Gewicht zu Slicer-Länge berechnet (stimmt auch bei abgebrochenen Drucken), ohne Slicer-Angaben über die Dichte des Filamenttyps bei 1,75 mm.

| Typ | Drucker | Adresse |
|---|---|---|
| `Moonraker` | Klipper, z. B. Voron, Snapmaker U1 mit paxx12-Mod | `http://<drucker>:7125` (oder über Fluidd/Mainsail-Port); `apiKey` nur bei aktivierter Moonraker-Authentifizierung |

Weitere Typen (Bambu LAN, PrusaLink, Anycubic) folgen.

## Datenbank-Migrationen

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Filomat.Api -o Data/Migrations
```

Migrationen werden beim Start automatisch angewendet.
