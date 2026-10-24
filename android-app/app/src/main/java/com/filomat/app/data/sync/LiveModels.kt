package com.filomat.app.data.sync

import kotlinx.serialization.Serializable
import java.util.Calendar
import java.util.Date
import java.util.Locale

/** Zustand eines Druckers mit den Spulen der App und Hinweisen (`/api/printers/status`). */
@Serializable
data class PrinterLive(
    val printerId: String,
    val name: String,
    val type: String = "",
    val updatedAt: String? = null,
    val status: PrinterStatus = PrinterStatus(),
    val tools: List<ToolLive> = emptyList(),
    val hints: List<PrinterHint> = emptyList()
) {
    val updatedDate: Date? get() = updatedAt?.let(BackendDate::parse)
    val warnings: List<PrinterHint> get() = hints.filter { it.warning }
}

@Serializable
data class PrinterStatus(
    /** `Idle`, `Printing`, `Paused`, `Finished`, `Cancelled`, `Error`, `Offline` */
    val state: String = "Idle",
    val jobName: String? = null,
    val jobKey: String? = null,
    /** 0–100 */
    val progress: Double? = null,
    val remainingSeconds: Int? = null,
    val elapsedSeconds: Int? = null,
    val layer: Int? = null,
    val totalLayers: Int? = null,
    val activeTool: Int? = null,
    val heaters: List<Heater> = emptyList(),
    val slots: List<SlotReport> = emptyList(),
    val dryers: List<DryerReport> = emptyList(),
    val jobGramsPerTool: List<Double>? = null,
    val jobGramsEstimated: Boolean = false,
    val message: String? = null,
    val hasThumbnail: Boolean = false
) {
    val isActive: Boolean get() = state == "Printing" || state == "Paused"
    val hasJob: Boolean get() = isActive || state == "Finished" || state == "Cancelled"

    val title: String
        get() = when (state) {
            "Printing" -> "Druckt"
            "Paused" -> "Pausiert"
            "Finished" -> "Fertig"
            "Cancelled" -> "Abgebrochen"
            "Error" -> "Fehler"
            "Offline" -> "Nicht erreichbar"
            else -> "Bereit"
        }

    /** "noch 1:23 h" bzw. "noch 12 min" */
    val remainingText: String?
        get() = remainingSeconds?.takeIf { isActive && it > 0 }?.let { "noch " + duration(it) }

    /** "Druckt benchy · 45 % · noch 32 min" */
    val summary: String
        get() {
            var head = title
            if (hasJob && !jobName.isNullOrEmpty()) head += " $jobName"
            val parts = mutableListOf(head)
            if (isActive) progress?.let { parts += "${it.toInt()} %" }
            remainingText?.let { parts += it }
            return parts.joinToString(" · ")
        }

    companion object {
        fun duration(seconds: Long): String =
            if (seconds >= 3600) String.format(Locale.US, "%d:%02d h", seconds / 3600, seconds % 3600 / 60)
            else "${maxOf(seconds / 60, 1)} min"

        fun duration(seconds: Int): String = duration(seconds.toLong())
    }
}

@Serializable
data class Heater(val name: String, val tool: Int? = null, val actual: Double? = null, val target: Double? = null) {
    val text: String
        get() {
            val now = actual?.let { "${Math.round(it)} °C" } ?: "–"
            return if (target != null && target > 0) "$now / ${Math.round(target)} °C" else now
        }
}

@Serializable
data class SlotReport(
    val tool: Int,
    val loaded: Boolean? = null,
    val type: String? = null,
    val colorHex: String? = null,
    val remainPercent: Double? = null,
    val tagUid: String? = null
)

@Serializable
data class DryerReport(
    val name: String,
    val humidityPercent: Double? = null,
    /** Bambu: 1 (feucht) bis 5 (trocken) */
    val humidityLevel: Int? = null,
    val temperature: Double? = null,
    val tools: List<Int> = emptyList()
) {
    val text: String
        get() = listOfNotNull(
            humidityPercent?.let { "${Math.round(it)} % Feuchte" } ?: humidityLevel?.let { "Feuchtestufe $it/5" },
            temperature?.let { "${Math.round(it)} °C" }
        ).joinToString(" · ")
}

@Serializable
data class ToolLive(
    val tool: Int,
    val place: String = "",
    val spoolId: String? = null,
    val spoolNumber: Int? = null,
    val itemId: String? = null,
    val itemName: String? = null,
    val type: String? = null,
    val colorHex: String? = null,
    val remainingGrams: Double? = null,
    val reported: SlotReport? = null,
    val neededGrams: Double? = null,
    val enough: Boolean? = null
) {
    /** "PLA Schwarz #2", was der Drucker meldet, oder "leer". */
    val title: String
        get() = when {
            itemName != null -> spoolNumber?.let { "$itemName #$it" } ?: itemName
            reported?.type != null && reported.loaded != false -> reported.type
            else -> "leer"
        }
}

@Serializable
data class PrinterHint(
    /** `SpoolShort`, `TagMismatch`, `SlotMismatch`, `SlotEmpty`, `SlotUnknown`, `Temperature`, `Humidity` */
    val kind: String,
    val warning: Boolean = false,
    val tool: Int? = null,
    val text: String
)

// Statistik

@Serializable
data class StatsTotals(
    val grams: Double = 0.0,
    val jobs: Int = 0,
    val completed: Int = 0,
    val cancelled: Int = 0,
    val printSeconds: Long = 0,
    val cancelledGrams: Double = 0.0
)

@Serializable
data class StatsMonth(val month: String, val grams: Double = 0.0, val jobs: Int = 0, val printSeconds: Long = 0) {
    /** "Okt." aus "2026-10", mit [withYear] "10/26" */
    fun label(withYear: Boolean): String {
        val parts = month.split("-")
        val number = parts.getOrNull(1)?.toIntOrNull() ?: return month
        if (withYear) return "${parts[1]}/${parts[0].takeLast(2)}"
        val calendar = Calendar.getInstance().apply { set(Calendar.DAY_OF_MONTH, 1); set(Calendar.MONTH, number - 1) }
        return calendar.getDisplayName(Calendar.MONTH, Calendar.SHORT, Locale.getDefault()) ?: month
    }
}

@Serializable
data class StatsPrinter(val printerId: String, val name: String, val totals: StatsTotals = StatsTotals())

@Serializable
data class StatsItem(
    val itemId: String? = null,
    val name: String,
    val type: String? = null,
    val colorHex: String? = null,
    val grams: Double = 0.0,
    val jobs: Int = 0
)

@Serializable
data class StatsType(val type: String, val grams: Double = 0.0)

@Serializable
data class StatsResponse(
    val from: String = "",
    val totals: StatsTotals = StatsTotals(),
    val months: List<StatsMonth> = emptyList(),
    val printers: List<StatsPrinter> = emptyList(),
    val items: List<StatsItem> = emptyList(),
    val types: List<StatsType> = emptyList()
)

// Meldungen

@Serializable
data class NotificationSettings(
    val url: String? = null,
    val hasToken: Boolean = false,
    /** `false`, wenn die Adresse aus der Server-Konfiguration kommt */
    val fromApp: Boolean = false,
    val configured: Boolean = false
)

/** [token] `null` lässt den gespeicherten Token, ein leerer Text löscht ihn. */
@Serializable
data class NotificationSettingsRequest(val url: String?, val token: String? = null)

/** "1,2 kg" ab 1000 g, sonst "340 g" */
fun gramsText(grams: Double): String =
    if (grams >= 1000) String.format(Locale.GERMANY, "%.1f kg", grams / 1000) else "${Math.round(grams)} g"
