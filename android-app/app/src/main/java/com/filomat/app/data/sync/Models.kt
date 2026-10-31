package com.filomat.app.data.sync

import com.filomat.app.data.Container
import com.filomat.app.data.ItemJsonData
import kotlinx.serialization.Serializable
import java.text.ParseException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import com.filomat.app.i18n.tr

/** Container im Austauschformat des Backends (`/api/snapshot`, `/api/import`). */
@Serializable
data class SnapshotContainer(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String = "",
    val capacity: Int? = null,
    val createdAt: Long,
    val isStandard: Boolean = false
)

/** Filament (Sorte) im Austauschformat des Backends. Die Gewichte pflegt das Backend. */
@Serializable
data class SnapshotItem(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String = "",
    val createdAt: Long,
    val containerId: String? = null,
    val itemData: ItemJsonData? = null,
    val netWeight: Double? = null,
    val spoolWeight: Double? = null,
    val spoolsInStock: Int? = null,
    /** Lichtdurchlässigkeit (TD) in mm; ältere Backends kennen sie nicht. */
    val transmissionDistance: Double? = null,
    /** Weitere Tags derselben Sorte; ältere Backends kennen sie nicht. */
    val extraTags: List<String>? = null
)

/** Alle Container und Filamente; `spools` liefert nur das Backend, beim Import wird es ignoriert. */
@Serializable
data class Snapshot(
    val containers: List<SnapshotContainer>,
    val items: List<SnapshotItem>,
    val spools: List<Spool>? = null
)

@Serializable
data class MergeRequest(val fromId: String)

/** Spule, die beim Zusammenführen eine neue Nummer bekommen hat. */
@Serializable
data class RenumberedSpool(val oldNumber: Int, val newNumber: Int)

@Serializable
data class MergeResult(val renumbered: List<RenumberedSpool> = emptyList())

@Serializable
data class ImportResult(val containers: Int, val items: Int, val skipped: List<String> = emptyList())

/** Eine angebrochene Spule einer Sorte. Die Nummer steht mit Stift auf der Spule. */
@Serializable
data class Spool(
    val id: String,
    val itemId: String,
    val number: Int,
    val remainingWeight: Double? = null,
    /** Drucker, in dem die Spule steckt; `null` heißt Lager. */
    val printerId: String? = null,
    /** Kopf bzw. Slot am Drucker (0-basiert). */
    val tool: Int? = null,
    /** Container, in dem die Spule im Lager liegt; ältere Backends liefern den Wert nicht. */
    val containerId: String? = null
) {
    val place: SpoolPlace
        get() = SpoolPlace(
            printerId = printerId,
            tool = if (printerId == null) null else tool ?: 0,
            containerId = if (printerId == null) containerId else null
        )

    /** "U1 Kopf 2", "Prusa", "Regal oben" oder "Lager". */
    fun locationText(printers: List<Printer>, containers: List<Container> = emptyList()): String =
        place.text(printers, containers)

    fun movedTo(place: SpoolPlace): Spool = copy(printerId = place.printerId, tool = place.tool, containerId = place.containerId)
}

/** Platz einer angebrochenen Spule: ein Kopf eines Druckers oder das Lager, dort optional ein Container. */
data class SpoolPlace(val printerId: String? = null, val tool: Int? = null, val containerId: String? = null) {
    fun text(printers: List<Printer>, containers: List<Container> = emptyList()): String {
        if (printerId != null) {
            val printer = printers.firstOrNull { it.id == printerId } ?: return tr("Unbekannter Drucker")
            return printer.placeName(tool)
        }
        containerId?.let { id -> containers.firstOrNull { it.id == id }?.let { return it.name } }
        return tr("Lager")
    }

    companion object {
        val STORAGE = SpoolPlace()
        fun printer(id: String, tool: Int) = SpoolPlace(printerId = id, tool = tool)
        fun container(id: String) = SpoolPlace(containerId = id)
    }
}

/** Anfrage für `POST /api/items/{id}/spools` und `PUT /api/spools/{id}`. */
@Serializable
data class SpoolUpdate(
    val remainingWeight: Double? = null,
    val printerId: String? = null,
    val tool: Int? = null,
    val containerId: String? = null
)

enum class PrinterType(
    val raw: String,
    val title: String,
    val addressHint: String,
    val setupHint: String,
    /** Bambu verlangt den Zugangscode, die anderen einen optionalen API-Schlüssel. */
    val requiresKey: Boolean = false
) {
    MOONRAKER(
        "Moonraker", "Klipper / Moonraker", "http://voron.local:7125",
        tr("Der Verbrauch wird von der Spule abgezogen, die in der App an diesem Drucker eingelegt ist.")
    ),
    SNAPMAKER_U1(
        "SnapmakerU1", "Snapmaker U1 (paxx12)", "http://192.168.2.155",
        tr("Der U1 erkennt die Spulen in jedem Kopf über ihre Tags. Der Verbrauch wird pro Kopf abgezogen.")
    ),
    PRUSA_LINK(
        "PrusaLink", "Prusa (PrusaLink)", "http://prusa-core-one.local",
        tr("Den API-Schlüssel zeigt der Drucker unter Einstellungen > Netzwerk > PrusaLink. Mit MMU3 wird pro Slot abgezogen, von der Spule, die in der App an diesem Slot eingelegt ist.")
    ),
    ANYCUBIC(
        "Anycubic", tr("Anycubic Kobra (LAN-Modus)"), "http://192.168.2.160",
        tr("Am Drucker Einstellungen > Netzwerk > LAN-Modus einschalten (die Anycubic-Cloud ist dann aus). Mit ACE wird pro Slot abgezogen, von der Spule, die in der App an diesem Slot eingelegt ist.")
    ),
    BAMBU(
        "Bambu", "Bambu Lab P1S, X1, A1 (LAN)", "http://192.168.2.170",
        tr("Den Zugangscode zeigt der Drucker unter Einstellungen > WLAN, die Seriennummer liest das Backend selbst. Abgezogen wird das Slicer-Gewicht aus der Druckdatei, mit AMS pro Slot (Slot 1–4 AMS, Slot 5 externe Spule), von der Spule, die in der App an diesem Slot eingelegt ist."),
        requiresKey = true
    );

    val keyLabel: String get() = if (requiresKey) tr("Zugangscode") else tr("API-Schlüssel (optional)")

    companion object {
        fun of(raw: String): PrinterType? = entries.firstOrNull { it.raw == raw }
    }
}

/** Im Backend konfigurierter Drucker. Der Typ bleibt Text, damit neue Druckertypen des Backends die App nicht stören. */
@Serializable
data class Printer(
    val id: String,
    val name: String,
    val type: String,
    val baseUrl: String,
    val hasApiKey: Boolean = false,
    val enabled: Boolean = true,
    val containerId: String = "",
    /** Anzahl Köpfe bzw. Slots (U1: 4); ältere Backends liefern den Wert nicht. */
    val tools: Int? = null,
    val lastSyncAt: String? = null,
    val lastSyncError: String? = null
) {
    val printerType: PrinterType? get() = PrinterType.of(type)
    val typeTitle: String get() = printerType?.title ?: type
    val toolCount: Int get() = maxOf(tools ?: 1, 1)
    val lastSyncDate: Date? get() = lastSyncAt?.let(BackendDate::parse)

    /** "U1 Kopf 2", "Core One Slot 3" oder nur der Name bei einem Kopf. */
    fun placeName(tool: Int?): String {
        if (toolCount <= 1 || tool == null) return name
        return if (printerType in listOf(PrinterType.PRUSA_LINK, PrinterType.ANYCUBIC, PrinterType.BAMBU)) tr("%s Slot %s", name, tool + 1)
        else tr("%s Kopf %s", name, tool + 1)
    }
}

@Serializable
data class PrinterRequest(
    val name: String,
    val type: String,
    val baseUrl: String,
    val apiKey: String? = null,
    val rfidTag: String? = null
)

/** Änderungen an einem Drucker; [apiKey] `null` lässt den Schlüssel, ein leerer Text löscht ihn. */
@Serializable
data class PrinterUpdateRequest(
    val name: String,
    val baseUrl: String,
    val apiKey: String? = null,
    val enabled: Boolean = true
)

@Serializable
data class SyncResponse(val recorded: Int, val error: String? = null)

/** Gebuchter Verbrauch eines Drucks. */
@Serializable
data class ConsumptionEntry(
    val id: String,
    val printerId: String,
    val jobName: String = "",
    val status: String = "",
    val endedAt: String,
    val grams: Double,
    val tool: Int? = null,
    val itemId: String? = null
) {
    val endedDate: Date? get() = BackendDate.parse(endedAt)
}

/** Stand der Backend-Version (`/api/update`). Zeitpunkte in Millisekunden seit 1970. */
@Serializable
data class BackendUpdateStatus(
    /** `false`, wenn auf dem Server kein Updater eingerichtet ist. */
    val supported: Boolean,
    val branch: String? = null,
    val currentVersion: String? = null,
    val currentDate: Long? = null,
    val latestVersion: String? = null,
    val updateAvailable: Boolean = false,
    val commits: List<UpdateCommit> = emptyList(),
    val checkedAt: Long? = null,
    /** `idle`, `requested`, `running` oder `failed` */
    val state: String = "idle",
    val message: String? = null
) {
    val isUpdating: Boolean get() = state == "requested" || state == "running"
    val failed: Boolean get() = state == "failed"
}

@Serializable
data class UpdateCommit(val sha: String, val subject: String, val date: Long)

/** API-Stufe des Backends (`/api/version`). */
@Serializable
data class BackendVersion(val apiLevel: Int, val minAppLevel: Int = 0, val commit: String? = null)

/** Passen App und Backend zusammen? Die Stufen erklärt `backend/README.md` unter „Versionen“. */
enum class ApiCompatibility(private val text: String?) {
    COMPATIBLE(null),
    BACKEND_OUTDATED("Backend veraltet: bitte aktualisieren, sonst fehlen Funktionen."),
    APP_OUTDATED("App veraltet: bitte die neue Version installieren.");

    /** In der gewählten Sprache; übersetzt erst beim Lesen, weil die Sprache wechseln kann. */
    val message: String? get() = text?.let { tr(it) }

    companion object {
        /** API-Stufe, gegen die diese App gebaut ist. */
        const val APP_LEVEL = 1

        /** Kleinste Backend-Stufe, mit der diese App alles kann. */
        const val MIN_BACKEND_LEVEL = 1

        /** `version == null`: das Backend kennt `/api/version` noch nicht (Stufe 0). */
        fun of(version: BackendVersion?, appLevel: Int = APP_LEVEL, minBackendLevel: Int = MIN_BACKEND_LEVEL): ApiCompatibility = when {
            appLevel < (version?.minAppLevel ?: 0) -> APP_OUTDATED
            (version?.apiLevel ?: 0) < minBackendLevel -> BACKEND_OUTDATED
            else -> COMPATIBLE
        }
    }
}

/** Datumswerte aus dem Backend (ISO 8601, .NET liefert bis zu 7 Nachkommastellen). */
object BackendDate {
    private val pattern = Regex("""^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:\d{2})?$""")

    fun parse(text: String): Date? {
        val match = pattern.matchEntire(text.trim()) ?: return null
        val (base, fraction, zone) = match.destructured
        val millis = fraction.take(3).padEnd(3, '0')
        // Ohne Zeitzone liefert .NET UTC-Zeitpunkte
        val offset = if (zone.isEmpty() || zone == "Z") "+00:00" else zone
        val format = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSSXXX", Locale.US)
        format.timeZone = TimeZone.getTimeZone("UTC")
        return try {
            format.parse("$base.$millis$offset")
        } catch (e: ParseException) {
            null
        }
    }
}
