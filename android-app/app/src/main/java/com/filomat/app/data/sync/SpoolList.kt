package com.filomat.app.data.sync

import com.filomat.app.data.Item
import com.filomat.app.i18n.tr

/** Welche Spulen die Spulenliste zeigt (wie die Auswahl in der Web-Oberfläche und der iOS-App). */
enum class SpoolFilter(val title: String) {
    ALL(tr("Alle")),
    PRINTER(tr("Im Drucker")),
    STORAGE(tr("Im Lager"))
}

/** Eine Zeile der Spulenliste: die Spule mit ihrer Sorte und ihrem Platz. */
data class SpoolListEntry(
    val spool: Spool,
    val item: Item?,
    val title: String,
    val place: String,
    /** Restgewicht; ohne gewogenen Wert rechnet das Backend mit einer vollen Spule der Sorte. */
    val remaining: Double?,
    /** Anteil des Füllgewichts einer neuen Spule (0..1), wenn beides bekannt ist. */
    val fraction: Double?
) {
    val isWeighed: Boolean get() = spool.remainingWeight != null
}

/** Spulen gefiltert und durchsucht (Sorte, Farbe, Platz, "#3"), sortiert nach Sorte und Nummer. */
fun StoreState.spoolEntries(query: String = "", filter: SpoolFilter = SpoolFilter.ALL): List<SpoolListEntry> {
    val q = query.trim().lowercase()
    return spools
        .filter {
            when (filter) {
                SpoolFilter.ALL -> true
                SpoolFilter.PRINTER -> it.printerId != null
                SpoolFilter.STORAGE -> it.printerId == null
            }
        }
        .map { spool ->
            val item = this@spoolEntries.item(spool.itemId)
            val remaining = spool.remainingWeight ?: item?.netWeight
            val net = item?.netWeight
            SpoolListEntry(
                spool = spool,
                item = item,
                title = item?.getDisplayTitle() ?: tr("Filament"),
                place = spool.locationText(printers, containers),
                remaining = remaining,
                fraction = if (remaining != null && net != null && net > 0) minOf(1.0, remaining / net) else null
            )
        }
        .filter { entry ->
            q.isEmpty() || listOf(entry.title, entry.place, entry.item?.itemData?.color, "#${entry.spool.number}")
                .any { it?.lowercase()?.contains(q) == true }
        }
        .sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { e: SpoolListEntry -> e.title }.thenBy { it.spool.number })
}
