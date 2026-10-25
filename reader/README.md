# NFC-Leser am Drucker

Ein ESP32 mit NFC-Leser meldet dem Backend, welches Musterplättchen in welchem Slot eines Druckers steckt, so wie der Snapmaker U1 es mit seinen Köpfen selbst tut. Spule einlegen, Plättchen in den Halter stecken, fertig: das Backend legt die Spule der Sorte in diesen Slot und bucht den Verbrauch, den PrusaLink, Bambu oder Anycubic melden, auf sie. Gelesen wird nur die UID; was das Plättchen ist, weiß das Backend.

| Datei | Für | Hardware |
| --- | --- | --- |
| [`fach-5x-rc522.yaml`](fach-5x-rc522.yaml) | Core One mit MMU3 | ESP32-DevKit + 5 RC522, ein Leser pro Fach |
| [`fach-1x-pn532.yaml`](fach-1x-pn532.yaml) | P1S ohne AMS, MK4S, Mini | ESP32-C3 mit 0,42"-OLED (HW-675) + PN532 |
| [`tippen-pn532.yaml`](tippen-pn532.yaml) | Kobra X mit ACE (8 Slots) | ESP32-C3 mit 0,42"-OLED + PN532, Slot per BOOT-Taste |

`common.yaml` (WLAN, Updates, Backend-Adresse) und `c3-display.yaml` (C3-Board, Display, I²C) werden eingebunden.

## Einrichten

1. ESPHome installieren: `pip install esphome` (oder das ESPHome-Dashboard in Docker).
2. `secrets.example.yaml` nach `secrets.yaml` kopieren und WLAN, Backend-Adresse und gegebenenfalls `Filomat:ApiKey` eintragen.
3. In der Datei des Druckers `printer_id` setzen: die Id aus `GET /api/printers` (im Browser `http://<Backend>/api/printers`, Feld `id` beim Drucker). Für mehrere Drucker derselben Art eine Kopie mit eigenem `name` anlegen, z. B. `fach-1x-pn532.yaml` als `mk4s.yaml` mit `name: filomat-mk4s`.
4. Per USB flashen: `esphome run fach-1x-pn532.yaml`. Danach gehen Updates per WLAN mit demselben Befehl.
5. Log ansehen: `esphome logs fach-1x-pn532.yaml`. Jede Meldung ans Backend steht dort mit Antwort.

## Verdrahtung

**PN532 am ESP32-C3 (I²C, zusammen mit dem Display):** Schalter am PN532 auf I²C (1 = ON, 2 = OFF).

| PN532 | VCC | GND | SDA | SCL |
| --- | --- | --- | --- | --- |
| ESP32-C3 | 3V3 | GND | GPIO5 | GPIO6 |

**5 RC522 am ESP32-DevKit (SPI):** alle Leser parallel an SCK, MOSI, MISO, 3,3 V und GND; nur SDA (= CS) einzeln. RST an 3,3 V, IRQ frei. Nie an 5 V.

| RC522 | 3.3V | GND | SCK | MOSI | MISO | SDA Slot 1 | Slot 2 | Slot 3 | Slot 4 | Slot 5 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ESP32 | 3V3 | GND | GPIO18 | GPIO23 | GPIO19 | GPIO26 | GPIO17 | GPIO16 | GPIO4 | GPIO13 |

## Bedienung

- **Fach-Leser:** Plättchen einstecken = Spule eingelegt. Herausziehen ändert nichts am Bestand (die Spule bleibt im Slot, bis ein anderes Plättchen kommt oder sie in der App umgelegt wird). Das Display am C3 zeigt die Sorte mit Spulennummer, „Unbekannt“ bei einem Plättchen ohne Filament (dazu kommt eine ntfy-Meldung) oder einen Fehler, wenn das Backend nicht antwortet.
- **Tipp-Modus (Kobra):** Plättchen an den Leser halten, das Display zeigt die Sorte. BOOT-Taste drücken, jeder Druck einen Slot weiter (1–4 Rack, 5–8 ACE). 4 s nach dem letzten Druck kommt die Spule in den Slot, das Display bestätigt. Ohne Tastendruck bricht der Leser nach 15 s ab.

## Worauf achten

- **Magnete:** Die Neodym-Magnete im Tag-Halter sitzen nah an der Antenne und können die Reichweite stark senken. Erst mit einem Leser ausprobieren; notfalls die Magnete an den Rand außerhalb der Antennenspule setzen.
- **Mehrere Leser nebeneinander** (MMU3): Die RC522 können sich gegenseitig stören, wenn ihre Antennen eng beieinander liegen. Wird ein Plättchen nicht erkannt oder „flackert“ im Log, den Abstand vergrößern oder `update_interval` erhöhen.
- **RC522** ist höher (60 mm) als ein Fach des Halters (45 mm); der Halter braucht eine Tasche dafür.
