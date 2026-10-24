# iOS-App über TestFlight

Der Workflow [`.github/workflows/ios-testflight.yml`](../.github/workflows/ios-testflight.yml) baut die App signiert und lädt sie zu TestFlight hoch. Er läuft bei jedem Push auf `main`, der `ios-app/` ändert, und lässt sich unter *Actions → iOS TestFlight → Run workflow* von Hand starten. Solange die Secrets fehlen, überspringt er den Upload mit einer Warnung.

Die Build-Nummer ist die Laufnummer des Workflows, die Version (`MARKETING_VERSION`, derzeit 1.0) steht im Xcode-Projekt.

## Einmalige Einrichtung

### 1. Apple Developer Program

TestFlight braucht die bezahlte Mitgliedschaft (99 €/Jahr) unter <https://developer.apple.com/programs/>. Mit einem kostenlosen „Personal Team“ geht es nicht.

### 2. App-ID

Unter <https://developer.apple.com/account/resources/identifiers/list> muss es die App-ID `com.filomat.app` mit der Capability **NFC Tag Reading** geben. Hat Xcode die App schon einmal aufs iPhone gebracht, ist sie meist schon da; sonst mit *+ → App IDs → App* anlegen und *NFC Tag Reading* anhaken.

### 3. App in App Store Connect

<https://appstoreconnect.apple.com/apps> → *+ → Neue App*: Plattform iOS, ein Name (muss im App Store einmalig sein, z. B. „Fil-o-mat Filament“), Sprache Deutsch, Bundle-ID `com.filomat.app`, SKU z. B. `filomat`. Veröffentlicht wird dadurch nichts.

### 4. Verteilungszertifikat (.p12)

Am Mac in Xcode: *Settings → Accounts →* Team wählen *→ Manage Certificates → + → Apple Distribution*. Danach in der Schlüsselbundverwaltung unter *Meine Zertifikate* „Apple Distribution: …“ rechtsklicken → *Exportieren* als `.p12` mit einem Passwort.

### 5. Provisioning-Profil

<https://developer.apple.com/account/resources/profiles/list> → *+ → App Store Connect* (unter Distribution) → App-ID `com.filomat.app` → das Zertifikat aus Schritt 4 → Name z. B. `Filomat App Store` → herunterladen. Team, Bundle-ID und Profilname liest der Workflow aus dem Profil.

### 6. App-Store-Connect-API-Schlüssel

<https://appstoreconnect.apple.com/access/integrations/api> → *Team-Schlüssel → +*, Name z. B. `GitHub Actions`, Zugriff **App Manager**. Die `.p8`-Datei lässt sich nur einmal herunterladen. Notieren: *Schlüssel-ID* und oben auf der Seite die *Issuer-ID*.

### 7. Secrets in GitHub

Unter *Settings → Secrets and variables → Actions → New repository secret* im Repo:

| Secret | Inhalt |
| --- | --- |
| `IOS_DIST_CERT_P12_BASE64` | `base64 -i Zertifikat.p12 \| pbcopy` |
| `IOS_DIST_CERT_PASSWORD` | Passwort aus Schritt 4 |
| `IOS_PROVISION_PROFILE_BASE64` | `base64 -i Filomat_App_Store.mobileprovision \| pbcopy` |
| `ASC_KEY_ID` | Schlüssel-ID aus Schritt 6 |
| `ASC_ISSUER_ID` | Issuer-ID aus Schritt 6 |
| `ASC_PRIVATE_KEY` | Inhalt der `.p8`-Datei (`cat AuthKey_XXXX.p8 \| pbcopy`), mit den BEGIN/END-Zeilen |

### 8. Erster Build und Tester

*Actions → iOS TestFlight → Run workflow*. Nach dem Upload verarbeitet Apple den Build 5–15 Minuten. Dann in App Store Connect unter *TestFlight → Interne Tests* eine Gruppe anlegen und sich selbst hinzufügen. Auf dem iPhone die App „TestFlight“ aus dem App Store installieren, dort erscheint Fil-o-mat. Interne Tester brauchen keine Prüfung durch Apple.

Weil die Bundle-ID gleich bleibt, ersetzt der TestFlight-Build die per Xcode installierte App; die lokalen Daten bleiben dabei normalerweise erhalten und liegen ohnehin auch im Backend.

## Gut zu wissen

- TestFlight-Builds laufen nach 90 Tagen ab; ein neuer Push auf `main` oder ein Handstart liefert einen neuen.
- Zertifikat und Profil gelten ein Jahr. Danach Schritte 4, 5 und 7 wiederholen.
- `ITSAppUsesNonExemptEncryption = NO` steht in der `Info.plist`, damit App Store Connect nicht bei jedem Build nach Exportkontrolle fragt (die App nutzt nur Standard-HTTPS).
