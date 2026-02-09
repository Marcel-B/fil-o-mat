# Fil-o-mat - Container- und Item-Verwaltung mit RFID

Eine iPhone Mobile App zur Verwaltung von Containern und deren Inhalten mit RFID-Tag-Unterstützung.

## Features

- **Container-Verwaltung**: Erstellen, bearbeiten und löschen von Containern mit RFID-Tags
- **Item-Verwaltung**: Verwalten von Items mit RFID-Tags
- **NFC/RFID-Scanning**: Lesen und Schreiben von NFC/RFID-Tags
- **Einlagern**: Container scannen, dann Item scannen, um Items in Container einzulagern
- **Herausnehmen**: Item scannen, dann Container scannen, um Items aus Containern zu entfernen
- **Tag-Beschreibung**: Tags mit Namen und Beschreibungen versehen
- **Persistenz**: Daten werden lokal gespeichert

## Technologie

- **SwiftUI**: Moderne UI-Framework für iOS
- **CoreNFC**: NFC-Reader-Integration für iPhone
- **UserDefaults**: Lokale Datenspeicherung

## Anforderungen

- iOS 17.0 oder höher
- iPhone mit NFC-Unterstützung (iPhone 7 oder neuer)
- Xcode 15.0 oder höher

## Installation

1. Öffnen Sie das Projekt in Xcode:
   ```bash
   open Fil-o-mat.xcodeproj
   ```

2. Wählen Sie ein iPhone-Simulator oder ein physisches Gerät als Ziel

3. Stellen Sie sicher, dass die NFC-Capability aktiviert ist:
   - Wählen Sie das Projekt in Xcode
   - Gehen Sie zu "Signing & Capabilities"
   - Fügen Sie "Near Field Communication Tag Reading" hinzu

4. Build und Run (⌘R)

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

## Tests

Die App enthält Unit Tests für:
- ContainerStore (Datenverwaltung)
- NFCReader (NFC-Funktionalität)

Um die Tests auszuführen:
1. Öffnen Sie das Projekt in Xcode
2. Drücken Sie ⌘U oder wählen Sie "Product" > "Test"

## Projektstruktur

```
Fil-o-mat/
├── Fil-o-mat/
│   ├── App.swift                 # App-Einstiegspunkt
│   ├── ContentView.swift         # Haupt-View mit TabView
│   ├── Models/
│   │   ├── Container.swift       # Container-Datenmodell
│   │   └── Item.swift            # Item-Datenmodell
│   ├── Services/
│   │   ├── ContainerStore.swift  # Datenverwaltung
│   │   └── NFCReader.swift       # NFC-Reader-Service
│   └── Views/
│       ├── ContainerListView.swift    # Container-Liste
│       ├── ContainerDetailView.swift   # Container-Details
│       ├── ScanView.swift             # Scan-Interface
│       └── TagDescriptionView.swift   # Tag-Verwaltung
└── Fil-o-matTests/
    ├── ContainerStoreTests.swift      # Tests für ContainerStore
    └── NFCReaderTests.swift           # Tests für NFCReader
```

## Hinweise

- NFC-Funktionalität funktioniert nur auf physischen Geräten, nicht im Simulator
- Stellen Sie sicher, dass NFC auf Ihrem iPhone aktiviert ist
- Die App verwendet NDEF-Format für NFC-Tags
- Tags, die NDEF nicht unterstützen, erhalten automatisch eine generierte UUID

## Lizenz

Dieses Projekt ist für den persönlichen Gebrauch erstellt.
