package com.filomat.app.data

import com.filomat.app.data.sync.Spool

/** Vorrat einer Sorte: volle Spulen und angebrochene Spulen mit Restgewicht. */
data class StockEntry(val item: Item, val openedSpools: List<Spool>) {
    val fullSpools: Int get() = item.spoolsInStock ?: 0

    /** Restgewicht der angebrochenen Spulen, soweit bekannt. */
    val openedGrams: Double get() = openedSpools.mapNotNull { it.remainingWeight }.sum()

    /** Filament insgesamt in Gramm; `null`, wenn weder Füllgewicht noch Restgewichte bekannt sind. */
    val totalGrams: Double?
        get() {
            val knowsOpened = openedSpools.any { it.remainingWeight != null }
            val net = item.netWeight ?: return if (knowsOpened) openedGrams else null
            return fullSpools * net + openedGrams
        }

    /**
     * Sollte nachgekauft werden: es wird Vorrat geführt, keine volle Spule ist mehr da und
     * die angebrochenen haben zusammen weniger als [threshold] Gramm (oder es gibt keine).
     */
    fun isLow(threshold: Double = Stock.LOW_THRESHOLD): Boolean {
        if (item.spoolsInStock == null || fullSpools != 0) return false
        if (openedSpools.isEmpty()) return true
        // Bei unbekanntem Rest lieber nicht warnen
        if (openedSpools.any { it.remainingWeight == null }) return false
        return openedGrams < threshold
    }
}

object Stock {
    /** Unter so viel Gramm gilt eine Sorte ohne volle Spulen als knapp. */
    const val LOW_THRESHOLD = 250.0

    /** Vorrat aller Sorten, nach Titel sortiert. */
    fun entries(items: List<Item>, spools: List<Spool>): List<StockEntry> {
        val byItem = spools.groupBy { it.itemId }
        return items
            .map { item -> StockEntry(item, (byItem[item.id] ?: emptyList()).sortedBy { it.number }) }
            .sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.item.getDisplayTitle() })
    }
}
