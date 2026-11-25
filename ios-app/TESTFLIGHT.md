# iOS-App über TestFlight

Der Workflow [`.gitea/workflows/ios-testflight.yml`](../.gitea/workflows/ios-testflight.yml) läuft in Gitea auf unseren eigenen Mac-Runnern (Label `macos`: Mac mini und oi-o-mat, ein Xcode, kein sudo) und baut die App für iPhone/iPad und für den Mac (Mac Catalyst) und lädt beide zu TestFlight hoch. Er läuft bei jedem Push auf `main`, der `ios-app/` ändert, und lässt sich unter *Actions → iOS TestFlight → Run workflow* von Hand starten. Solange die Secrets fehlen, überspringt er den Upload mit einer Warnung.

Signiert wird wie bei Tonwerk-AI per Cloud-Signierung: Das Archiv entsteht nur ad hoc signiert (damit die NFC-Entitlements drinstecken), beim Export erzeugt Xcode über den App-Store-Connect-API-Schlüssel das verwaltete Distribution-Zertifikat und das Profil selbst. Zertifikate oder Profile muss man nicht verwalten. Vor dem Upload prüft der Workflow, ob die signierte App die NFC-Berechtigung hat.

Build-Nummer: 2000 + Laufnummer des Workflows in Gitea (unter GitHub Actions waren es 1000 + Laufnummer bis 1018; von Hand in Xcode archivierte Builds bleiben darunter). Die Version (`MARKETING_VERSION`, derzeit 1.0) steht im Xcode-Projekt.

## Einrichtung

1. Die App existiert in App Store Connect (Bundle-ID `com.filomat.app`, Team `6GGU8ET2C4`).
2. Ein App-Store-Connect-API-Schlüssel mit Rolle **Admin** (nur damit darf Xcode verwaltete Zertifikate anlegen). Es kann derselbe wie für Tonwerk-AI sein.
3. Secrets im Gitea-Repo unter Einstellungen → Actions → Secrets: `ASC_KEY_ID` (die zehn Zeichen aus `AuthKey_<id>.p8`), `ASC_ISSUER_ID` und `ASC_KEY_P8` (Inhalt der .p8-Datei), dieselben Werte wie bei Tonwerk.

   Statt `ASC_KEY_P8` geht auch `ASC_PRIVATE_KEY`. `APPLE_TEAM_ID` ist optional; ohne das Secret nimmt der Workflow `6GGU8ET2C4`.
4. In App Store Connect unter *TestFlight → Interne Tests* eine Gruppe anlegen und sich selbst hinzufügen. Auf dem iPhone die App „TestFlight“ installieren, dort erscheint Fil-o-mat nach dem ersten Upload (Verarbeitung meist 5–15 Minuten). Interne Tester brauchen keine Prüfung durch Apple.

Weil die Bundle-ID gleich bleibt, ersetzt der TestFlight-Build die per Xcode installierte App; die lokalen Daten bleiben dabei normalerweise erhalten und liegen ohnehin auch im Backend.

## Mac

Die Mac-Version ist ein eigener Build für die Plattform macOS unter derselben App (gleiche Bundle-ID). Einmalig nötig:

1. In App Store Connect bei Fil-o-mat die Plattform **macOS** hinzufügen (auf der App-Seite *Plattform hinzufügen*), sonst lehnt der Upload den Mac-Build ab.
2. Auf dem Mac die App „TestFlight“ aus dem Mac App Store installieren; der Mac-Build erscheint dort für dieselbe interne Testgruppe.

Für den Mac legt Xcode beim Export zusätzlich ein verwaltetes Installer-Zertifikat an. Schlägt nur der Mac-Job fehl, läuft der iPhone/iPad-Build trotzdem durch (die beiden Jobs sind unabhängig).

## Gut zu wissen

- TestFlight-Builds laufen nach 90 Tagen ab; ein neuer Push auf `main` oder ein Handstart liefert einen neuen.
- `ITSAppUsesNonExemptEncryption = NO` steht in der `Info.plist`, damit App Store Connect nicht bei jedem Build nach Exportkontrolle fragt (die App nutzt nur Standard-HTTPS).
