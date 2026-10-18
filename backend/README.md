# Fil-o-mat – Backend

ASP.NET Core (.NET 10) mit SQLite. Gemeinsame Datenbasis für die Android- und die iOS-App und später die Anbindung der Drucker für das Verbrauchs-Tracking.

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
| PUT | `/api/items/{id}` | Name, Beschreibung, Tag-Daten ändern |
| POST | `/api/items/{id}/move` | Einlagern (`{"containerId": "…"}`) oder herausnehmen (`{"containerId": null}`) |
| DELETE | `/api/items/{id}` | Filament löschen |
| GET | `/health` | Lebenszeichen (ohne API-Schlüssel) |

Tags sind eindeutig: ein Tag gehört entweder zu genau einem Container oder zu genau einem Filament. Die Tag-Daten (`itemData`) verwenden dasselbe OpenSpool-Format wie auf dem NFC-Tag (`color_hex`, `min_temp` …).

## Datenbank-Migrationen

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Filomat.Api -o Data/Migrations
```

Migrationen werden beim Start automatisch angewendet.
