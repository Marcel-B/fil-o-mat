# Fil-o-mat - Android App

Eine Android Mobile App zur Verwaltung von Containern und deren Inhalten mit RFID/NFC-Tag-Unterstützung.

## Features

- **Container-Verwaltung**: Erstellen, bearbeiten und löschen von Containern mit RFID-Tags
- **Item-Verwaltung**: Verwalten von Items mit RFID-Tags
- **NFC/RFID-Scanning**: Lesen und Schreiben von NFC/RFID-Tags
- **Einlagern**: Container scannen, dann Item scannen, um Items in Container einzulagern
- **Herausnehmen**: Item scannen, dann Container scannen, um Items aus Containern zu entfernen
- **Tag-Beschreibung**: Tags mit Namen und Beschreibungen versehen
- **Persistenz**: Daten werden lokal mit DataStore gespeichert

## Technologie

- **Kotlin**: Programmiersprache
- **Jetpack Compose**: Moderne deklarative UI
- **Android NFC API**: NFC-Reader-Integration
- **DataStore**: Lokale Datenspeicherung

## Anforderungen

- Android 7.0 (API 24) oder höher
- Gerät mit NFC-Unterstützung
- Android Studio Hedgehog oder neuer

## Installation

1. Öffnen Sie das Projekt in Android Studio:
   - File → Open → Wählen Sie den `android-app` Ordner

2. Sync Gradle Files:
   - Android Studio wird automatisch die Abhängigkeiten herunterladen

3. Verbinden Sie ein Android-Gerät oder starten Sie einen Emulator

4. Build und Run (▶️)

## Verwendung

### Container erstellen

1. Öffnen Sie die App
2. Tippen Sie auf das "+" Symbol
3. Geben Sie einen Namen ein
4. Scannen Sie einen RFID-Tag oder geben Sie manuell einen Tag ein
5. Tippen Sie auf "Hinzufügen"

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

## Projektstruktur

```
android-app/
├── app/
│   └── src/
│       └── main/
│           ├── java/com/filomat/app/
│           │   ├── data/              # Datenmodelle
│           │   ├── nfc/                # NFC-Reader
│           │   ├── ui/
│           │   │   ├── navigation/     # Navigation
│           │   │   ├── screens/        # UI-Screens
│           │   │   ├── theme/          # Theme
│           │   │   └── viewmodel/      # ViewModels
│           │   └── MainActivity.kt
│           └── res/                   # Ressourcen
└── build.gradle.kts
```

## Hinweise

- NFC funktioniert nur auf physischen Geräten, nicht im Emulator
- Stellen Sie sicher, dass NFC auf Ihrem Android-Gerät aktiviert ist
- Die App verwendet NDEF-Format für NFC-Tags
- Tags, die NDEF nicht unterstützen, erhalten automatisch eine generierte ID basierend auf der Tag-ID

## Lizenz

Dieses Projekt ist für den persönlichen Gebrauch erstellt.
