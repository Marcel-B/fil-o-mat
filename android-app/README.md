# Fil-o-mat - Android App

Eine Android Mobile App zur Verwaltung von Containern und deren Inhalten mit RFID/NFC-Tag-Unterstützung.

## Features

- **Container-Verwaltung**: Erstellen, bearbeiten und löschen von Containern mit RFID-Tags
- **Item-Verwaltung**: Verwalten von Items mit RFID-Tags
- **NFC/RFID-Scanning**: Lesen und Schreiben von NFC/RFID-Tags
- **Scannen-Tab**: eine Liste von Abläufen wie in der iOS-App: Filament-Tag lesen, Container ansehen, Einlagern, In Drucker einlegen, Vorrat und Lichtdurchlässigkeit erfassen
- **Einlagern**: Musterplättchen scannen, wählen, ob die Sorte mit ihren vollen Spulen oder eine angebrochene Spule eingelagert wird, dann den Container scannen oder aus der Liste wählen
- **In Drucker einlegen**: Musterplättchen scannen, angebrochene Spule wählen oder eine neue anbrechen, dann Drucker und Kopf antippen; was dort steckte, kommt ins Lager (braucht das Backend)
- **Vorrat**: volle und angebrochene Spulen je Sorte mit Gesamtgewicht und einer Liste „Nachkaufen“ (keine volle Spule mehr und angebrochen unter 250 g)
- **Lichtdurchlässigkeit**: TD-Wert in mm pro Sorte (z. B. vom TD-1), beim Musterplättchen, in der Sorte, in „Tag lesen“ und in den Listen
- **Tag-Beschreibung**: Tags mit Namen und Beschreibungen versehen
- **Persistenz**: Daten werden lokal mit DataStore gespeichert
- **Backend-Abgleich**: Über das Wolken-Symbol wird die App mit dem [Backend](../backend/README.md) verbunden. Beim Verbinden werden die Daten des Geräts hochgeladen (nichts wird gelöscht) und danach der gemeinsame Stand übernommen; danach geht jede Änderung sofort ans Backend. Beim Öffnen und alle 2 Minuten, solange die App sichtbar ist, wird neu geladen (das Backend fragt die Drucker im selben Takt ab). Wie in der iOS-App.
- **Drucker**: Drucker im Backend anlegen (Snapmaker U1, PrusaLink, Moonraker), abgleichen und entfernen
- **Sorten und Spulen**: Ein Tipp auf ein Filament zeigt die Sorte mit Füll- und Leergewicht, Vorrat, angebrochenen Spulen (Nummer, Restgewicht, Platz im Drucker oder Lager, wiegen, leer melden) und dem gebuchten Verbrauch
- **Backend-Version**: Neue Commits anzeigen und das Backend aktualisieren
- **Filament-Tag lesen** (im Scannen-Tab): Zeigt alles an, was auf einem Tag steht (OpenSpool-Felder, auch fremde, NDEF-Records, Chip, UID) und ob die App ihn kennt; ändert nichts

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

### Filament einlagern

1. Gehen Sie zum Tab "Scannen" und wählen Sie "Einlagern"
2. Scannen Sie das Musterplättchen
3. Wählen Sie die Sorte (volle Spulen) oder eine angebrochene Spule
4. Scannen Sie den Container oder wählen Sie ihn aus der Liste
5. Tippen Sie auf "Einlagern"

Herausnehmen geht in der Container-Ansicht.

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

- Unter GrapheneOS braucht die App die Berechtigung „Netzwerk“, sonst scheitert der Abgleich mit „socket failed: EPERM“
- Ein Update der App über Android Studio behält die lokalen Daten; die App nie deinstallieren, solange sie nicht mit dem Backend abgeglichen ist

- NFC funktioniert nur auf physischen Geräten, nicht im Emulator
- Stellen Sie sicher, dass NFC auf Ihrem Android-Gerät aktiviert ist
- Die App verwendet NDEF-Format für NFC-Tags
- Tags, die NDEF nicht unterstützen, erhalten automatisch eine generierte ID basierend auf der Tag-ID

## Lizenz

Dieses Projekt ist für den persönlichen Gebrauch erstellt.
