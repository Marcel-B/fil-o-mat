# Fil-o-mat - Container- und Item-Verwaltung mit RFID

Apps zur Verwaltung von Containern und deren Inhalten (Filamentspulen) mit RFID/NFC-Tags:

- **Android**: Kotlin + Jetpack Compose in [`android-app/`](android-app/README.md)
- **iOS**: Swift + SwiftUI + CoreNFC in [`ios-app/`](ios-app/README.md)
- **Web**: Vue 3 + PrimeVue in [`web/`](web/README.md), läuft mit dem [Backend](backend/README.md) auf Port 8080 (ohne NFC)

Beide Apps verwenden dasselbe Tag-Format (OpenSpool-JSON) und erkennen Tags über dieselbe UID.

## Features

- **Container-Verwaltung**: Erstellen, bearbeiten und löschen von Containern mit RFID-Tags
- **Item-Verwaltung**: Verwalten von Items mit RFID-Tags
- **NFC/RFID-Scanning**: Lesen und Schreiben von NFC/RFID-Tags
- **Einlagern**: Container scannen, dann Item scannen, um Items in Container einzulagern
- **Herausnehmen**: Item scannen, dann Container scannen, um Items aus Containern zu entfernen
- **Tag-Beschreibung**: Tags mit Namen und Beschreibungen versehen
- **Persistenz**: Daten werden lokal gespeichert

## Installation

Siehe [`android-app/README.md`](android-app/README.md) bzw. [`ios-app/README.md`](ios-app/README.md).

## Verwendung

### Container erstellen

1. Öffnen Sie die App
2. Gehen Sie zum Tab "Container"
3. Tippen Sie auf das "+" Symbol
4. Geben Sie einen Namen ein
5. Scannen Sie einen RFID-Tag oder geben Sie manuell einen Tag ein
6. Tippen Sie auf "Hinzufügen"

### Items einlagern

1. Gehen Sie zum Tab "Scannen"
2. Wählen Sie "Einlagern"
3. Scannen Sie zuerst den Container
4. Scannen Sie dann das Item
5. Tippen Sie auf "Item einlagern"

### Items herausnehmen

1. Gehen Sie zum Tab "Scannen"
2. Wählen Sie "Herausnehmen"
3. Scannen Sie zuerst das Item
4. Scannen Sie dann den Container
5. Tippen Sie auf "Item herausnehmen"

### Tag verwalten

1. Gehen Sie zum Tab "Scannen"
2. Tippen Sie auf das Tag-Symbol oben rechts
3. Scannen Sie einen Tag
4. Geben Sie Name und Beschreibung ein
5. Wählen Sie zwischen Container oder Item
6. Speichern oder auf Tag schreiben

## Lizenz

Dieses Projekt ist für den persönlichen Gebrauch erstellt.
