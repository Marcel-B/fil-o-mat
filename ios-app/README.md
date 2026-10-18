# Fil-o-mat – iOS-App

Native iPhone-App (SwiftUI + CoreNFC) mit demselben Funktionsumfang wie die Android-App in `../android-app`.

## Anforderungen

- Xcode 16 oder neuer
- iPhone mit iOS 17 oder neuer (NFC funktioniert nur auf echten Geräten, nicht im Simulator)
- Apple-Developer-Konto (für die NFC-Berechtigung)

## Installation auf dem iPhone

1. `ios-app/Filomat.xcodeproj` in Xcode öffnen.
2. Target **Filomat** → *Signing & Capabilities*: unter *Team* das eigene Developer-Team wählen.
   Falls die Bundle-ID `com.filomat.app` schon vergeben ist, eine eigene eintragen (z. B. `de.<name>.filomat`).
3. Die Capability *Near Field Communication Tag Reading* ist über `Filomat/Filomat.entitlements` bereits eingetragen;
   Xcode legt die passende App-ID beim ersten Build automatisch an.
4. iPhone anschließen, als Ziel wählen und starten (⌘R).

## Abgleich mit dem Backend

Über das Wolken-Symbol in der Container-Liste lässt sich das [Backend](../backend/README.md) verbinden (z. B. `http://nas.local:8080`). Beim Verbinden werden die Daten des iPhones hochgeladen, ohne etwas zu löschen, und danach der gemeinsame Stand übernommen. Danach geht jede Änderung sofort ans Backend; beim Öffnen der App und per Herunterziehen der Liste wird neu abgeglichen.

Container und Filamente aus der Android-App kommen so aufs iPhone: in der Android-App über das Wolken-Symbol hochladen, danach auf dem iPhone verbinden.

## Drucker

Im Tab „Drucker“ werden die Drucker im Backend angelegt. Beim Snapmaker U1 erkennt der Drucker die Spulen selbst; bei anderen Druckern legst du die Spule in den Container des Druckers. Beim Bearbeiten eines Filaments trägst du Füll- und Restgewicht ein und siehst die gebuchten Drucke.

## Unterschiede zur Android-App

- **Scannen**: iOS zeigt bei jedem Scan das System-Fenster „Bereit zum Scannen“. Das Tag wird gelesen bzw. beschrieben, sobald es ans iPhone gehalten wird.
- **Container anlegen**: Beschreiben und Auslesen der UID passieren in einem Scan statt in zwei.
- **Unformatierte Tags**: iOS kann Tags nicht NDEF-formatieren. NTAG213/215/216 sind ab Werk formatiert und funktionieren direkt.
- **Schutz vor Verwechslungen**: Ein Tag, das schon zu einem Container oder Filament gehört, wird nicht versehentlich überschrieben.
- **Filament-Übersicht** zeigt alle Filamente, auch herausgenommene ohne Container.
- **Datenhaltung**: JSON-Datei im App-Container (Application Support). Die Daten werden nicht zwischen Android und iOS synchronisiert; die Tags selbst sind aber kompatibel (gleiche UID-Kennung, gleiches OpenSpool-JSON).

## Projektstruktur

```
ios-app/
├── Filomat.xcodeproj
├── Filomat/
│   ├── FilomatApp.swift          # Einstiegspunkt mit Tab-Leiste
│   ├── Models/                   # Container, Item (OpenSpool-Daten), Hex-Farben
│   ├── Data/                     # ContainerStore (Persistenz), Filament-Presets
│   ├── NFC/NFCService.swift      # CoreNFC lesen/schreiben
│   ├── Views/                    # SwiftUI-Screens
│   └── Resources/                # presets.json, brands.json, variants.json (Kopie aus der Android-App)
└── FilomatTests/                 # Unit-Tests (⌘U)
```

Die Preset-Dateien in `Filomat/Resources` sind Kopien aus `android-app/app/src/main/assets`. Bei Änderungen beide Stellen anpassen.
