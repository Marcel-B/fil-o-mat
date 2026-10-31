namespace Filomat.Api.Localization;

/// <summary>Englische Texte, Schlüssel ist der deutsche Text im Code. Neue Texte hier gleich mit eintragen.</summary>
public static class English
{
    public static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string>
    {
        // Hinweise beim Drucker
        ["Kopf {0}"] = "Head {0}",
        ["{0} hat noch {1}, der Druck braucht noch {2}."] = "{0} has {1} left, the print still needs {2}.",
        ["{0} hat noch {1}, der Druck braucht geschätzt noch {2}."] = "{0} has {1} left, the print still needs about {2}.",
        ["Der Drucker erkennt ein anderes Tag als {0}."] = "The printer detects a different tag than {0}.",
        ["Der Drucker erkennt {0}, in der App steckt dort {1}."] = "The printer detects {0}, the app has {1} there.",
        ["Laut Drucker leer, in der App steckt dort {0}."] = "Empty according to the printer, the app has {0} there.",
        ["{0} erkannt. Welche Spule steckt dort? Lose liegen {1}."] = "{0} detected. Which spool is in there? Loose: {1}.",
        ["{0} erkannt, in der App steckt dort keine Spule."] = "{0} detected, the app has no spool there.",
        ["{0} eingelegt, in der App ist dort nichts eingetragen."] = "{0} loaded, nothing is entered there in the app.",
        ["Der Drucker meldet {0}, in der App steckt dort {1} ({2})."] = "The printer reports {0}, the app has {1} ({2}) there.",
        ["Die Farbe im Drucker weicht von {0} ab."] = "The colour in the printer differs from {0}.",
        ["mindestens {0:0} °C"] = "at least {0:0} °C",
        ["höchstens {0:0} °C"] = "at most {0:0} °C",
        ["{0} heizt auf {1:0} °C, {2} #{3} will {4}. Falsches Profil?"] = "{0} is heating to {1:0} °C, {2} #{3} wants {4}. Wrong profile?",
        ["{0}: {1:0} % Feuchte, {2} sollte trocknen."] = "{0}: {1:0} % humidity, {2} should be dried.",
        ["{0}: {1:0} % Feuchte, das ist viel für Filament."] = "{0}: {1:0} % humidity, that is a lot for filament.",
        ["{0}: Feuchtestufe {1} von 5, {2} sollte trocknen. Trockenmittel wechseln?"] = "{0}: humidity level {1} of 5, {2} should be dried. Change the desiccant?",
        ["{0}: Feuchtestufe {1} von 5. Trockenmittel wechseln?"] = "{0}: humidity level {1} of 5. Change the desiccant?",
        ["Düse"] = "Nozzle",
        ["Bett"] = "Bed",

        // ntfy-Meldungen
        ["{0}: Fehler"] = "{0}: error",
        ["Der Druck {0} braucht Aufmerksamkeit."] = "The print {0} needs attention.",
        ["Der Drucker braucht Aufmerksamkeit."] = "The printer needs attention.",
        ["{0}: Spule reicht nicht"] = "{0}: spool won’t last",
        ["{0:0} g ohne Zuordnung"] = "{0:0} g unassigned",
        ["{0} −{1:0} g, Spule unklar"] = "{0} −{1:0} g, spool unclear",
        [", leer"] = ", empty",
        [", noch {0:0} g"] = ", {0:0} g left",
        [" (fast leer)"] = " (almost empty)",
        ["Druckzeit {0}:{1:00} h"] = "Print time {0}:{1:00} h",
        ["Druckzeit {0} min"] = "Print time {0} min",
        ["fertig"] = "finished",
        ["abgebrochen"] = "cancelled",
        ["mit Fehler beendet"] = "ended with an error",
        ["Druck"] = "Print",
        ["{0} ist {1}."] = "{0} is {1}.",
        ["{0}: Welche Spule?"] = "{0}: which spool?",
        ["{0} erkannt, lose liegen {1}."] = "{0} detected, loose: {1}.",
        ["Tippe auf die Spule, die drin steckt."] = "Tap the spool that is in there.",
        ["Wählen kannst du in der App beim Drucker."] = "You can choose in the app at the printer.",
        ["{0}: unbekanntes Plättchen"] = "{0}: unknown swatch",
        ["Das Plättchen {0} gehört zu keinem Filament."] = "The swatch {0} doesn’t belong to any filament.",
        ["In der App unter Filament bearbeiten > Plättchen hinzufügen zuordnen und neu einstecken."] =
            "Assign it in the app under Edit filament > Add swatch and insert it again.",
        ["Testmeldung: Meldungen kommen an."] = "Test message: notifications arrive.",

        // Fehler und Rückmeldungen der API
        ["Name fehlt."] = "Name is missing.",
        ["RFID-Tag fehlt."] = "RFID tag is missing.",
        ["Tag fehlt."] = "Tag is missing.",
        ["Slot ungültig."] = "Invalid slot.",
        ["Slots fehlen."] = "Slots are missing.",
        ["Container nicht gefunden."] = "Container not found.",
        ["Filament nicht gefunden."] = "Filament not found.",
        ["Spule nicht gefunden."] = "Spool not found.",
        ["Drucker nicht gefunden."] = "Printer not found.",
        ["Diesen Kopf hat der Drucker nicht."] = "The printer doesn’t have this head.",
        ["Gewicht darf nicht negativ sein."] = "Weight must not be negative.",
        ["Die Lichtdurchlässigkeit darf nicht negativ sein."] = "The transmission distance must not be negative.",
        ["Adresse muss mit http:// oder https:// beginnen."] = "Address must start with http:// or https://.",
        ["Adresse muss mit http:// oder https:// beginnen, z. B. https://ntfy.sh/mein-thema."] =
            "Address must start with http:// or https://, e.g. https://ntfy.sh/my-topic.",
        ["Zugangscode fehlt (am Drucker unter Einstellungen > WLAN)."] = "Access code is missing (on the printer under Settings > WLAN).",
        ["Container mit ID '{0}' existiert bereits."] = "Container with ID '{0}' already exists.",
        ["Filament mit ID '{0}' existiert bereits."] = "Filament with ID '{0}' already exists.",
        ["Dieses Tag gehört bereits zum Container '{0}'."] = "This tag already belongs to the container '{0}'.",
        ["Dieses Tag gehört zum Container '{0}'."] = "This tag belongs to the container '{0}'.",
        ["Dieses Tag gehört bereits zu einem Filament."] = "This tag already belongs to a filament.",
        ["Dieses Tag ist bereits vergeben."] = "This tag is already in use.",
        ["Container '{0}': Tag {1} ist bereits vergeben."] = "Container '{0}': tag {1} is already in use.",
        ["Filament '{0}': Tag {1} ist bereits vergeben."] = "Filament '{0}': tag {1} is already in use.",
        ["Standard-Container können nicht gelöscht werden."] = "Standard containers can’t be deleted.",
        ["Der Container gehört zum Drucker '{0}'. Bitte den Drucker entfernen."] = "The container belongs to the printer '{0}'. Please remove the printer.",
        ["Ein Filament lässt sich nicht mit sich selbst zusammenführen."] = "A filament can’t be merged with itself.",
        ["Dieser Verbrauch ist bereits einer Spule zugeordnet."] = "This consumption is already assigned to a spool.",
        ["Die Auswahl ist abgelaufen. Bitte in der App im Drucker wählen."] = "The choice has expired. Please choose in the app at the printer.",
        ["#{0} steckt jetzt in {1}."] = "#{0} is now in {1}.",
        ["Keine ntfy-Adresse eingestellt."] = "No ntfy address set.",
        ["ntfy antwortet nicht wie erwartet: {0}"] = "ntfy doesn’t answer as expected: {0}",
        ["Gelöschter Drucker"] = "Deleted printer",
        ["Ohne Zuordnung"] = "Unassigned",
        ["Es läuft schon eine Sicherung"] = "A backup is already running",
        ["Keine Nextcloud eingerichtet (NEXTCLOUD_WEBDAV_URL, NEXTCLOUD_USERNAME, NEXTCLOUD_APP_PASSWORD)"] =
            "No Nextcloud set up (NEXTCLOUD_WEBDAV_URL, NEXTCLOUD_USERNAME, NEXTCLOUD_APP_PASSWORD)",
        ["Die Aktualisierung läuft bereits."] = "The update is already running.",
        ["Es gibt keine neue Version."] = "There is no new version.",
        ["Auf dem Server ist kein Updater eingerichtet (deploy/updater/install.sh)."] = "No updater is set up on the server (deploy/updater/install.sh).",
    };

    /// <summary>
    /// Fertige deutsche Meldungen, die im Hintergrund entstehen und gespeichert werden (Drucker nicht erreichbar,
    /// Sicherung, Updater). <see cref="L.T"/> erkennt sie auch mit eingesetzten Werten.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
    {
        // Drucker
        ["Noch nicht abgefragt."] = "Not queried yet.",
        ["Pausiert."] = "Paused.",
        ["Düse {0}"] = "Nozzle {0}",
        ["Fehlercode {0}"] = "Error code {0}",
        ["Kein Adapter für Druckertyp {0}."] = "No adapter for printer type {0}.",
        ["Seriennummer unbekannt; bitte als Seriennummer:Zugangscode eintragen."] = "Serial number unknown; please enter it as serial:access code.",
        ["Der Drucker antwortet nicht auf MQTT-Abfragen (Seriennummer falsch?)."] = "The printer doesn’t answer MQTT requests (wrong serial number?).",
        ["Der Drucker antwortet nicht auf MQTT-Abfragen."] = "The printer doesn’t answer MQTT requests.",
        ["MQTT-Verbindung zum Drucker fehlgeschlagen: {0}"] = "MQTT connection to the printer failed: {0}",
        ["PrusaLink lehnt den API-Schlüssel ab (Einstellungen > Netzwerk > PrusaLink am Drucker)."] =
            "PrusaLink rejects the API key (Settings > Network > PrusaLink on the printer).",
        ["Der LAN-Modus ist am Drucker aus (Einstellungen > Netzwerk > LAN-Modus)."] = "LAN mode is off on the printer (Settings > Network > LAN mode).",
        ["Der Drucker unterstützt den LAN-Modus nicht (erst ab Kobra 3)."] = "The printer doesn’t support LAN mode (Kobra 3 and later only).",
        ["Der Drucker lehnt die Anmeldung ab: {0}"] = "The printer rejects the login: {0}",
        ["Antwort des Druckers ohne Zugangsdaten."] = "Printer answer without credentials.",
        ["Zugangsdaten ohne Broker."] = "Credentials without broker.",
        ["Zugangsdaten ohne Benutzer."] = "Credentials without user.",
        ["Zugangsdaten ohne Passwort."] = "Credentials without password.",
        ["Zugangsdaten ohne Geräte-ID."] = "Credentials without device ID.",

        // Sicherung in die Nextcloud
        ["Die Nextcloud antwortet nicht"] = "The Nextcloud doesn’t answer",
        ["Keine Nextcloud eingerichtet"] = "No Nextcloud set up",
        ["Nextcloud lehnt Benutzer oder App-Passwort ab (401)"] = "Nextcloud rejects the user or app password (401)",
        ["Der Ordner über dem Sicherungsordner fehlt in der Nextcloud (409)"] = "The folder above the backup folder is missing in the Nextcloud (409)",
        ["Sicherungsordner lässt sich nicht anlegen (HTTP {0})"] = "Backup folder can’t be created (HTTP {0})",
        ["Die Nextcloud ist voll (507)"] = "The Nextcloud is full (507)",
        ["Die Nextcloud hält {0} gesperrt (423)"] = "The Nextcloud keeps {0} locked (423)",
        ["{0} lässt sich nicht hochladen (HTTP {1})"] = "{0} can’t be uploaded (HTTP {1})",

        // Updater (deploy/updater/filomat-update.sh)
        ["GitHub nicht erreichbar, letzte Prüfung {0} ging nicht."] = "GitHub not reachable, the last check at {0} failed.",
        ["GitHub nicht erreichbar."] = "GitHub not reachable.",
        ["Aktualisierung läuft."] = "Update running.",
        ["git merge ging nicht (lokale Änderungen im Checkout?)."] = "git merge failed (local changes in the checkout?).",
        ["Neues Image ließ sich nicht bauen, die bisherige Version läuft weiter."] = "The new image couldn’t be built, the previous version keeps running.",
        ["Das Backend antwortet nach dem Update nicht (docker compose logs)."] = "The backend doesn’t answer after the update (docker compose logs).",
    };
}
