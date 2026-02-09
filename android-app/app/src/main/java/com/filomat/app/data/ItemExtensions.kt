package com.filomat.app.data

/**
 * Generiert den Titel für ein Filament im Format: <brand> <type> <variant>
 * Falls keine itemData vorhanden ist, wird der name verwendet.
 */
fun Item.getDisplayTitle(): String {
    val parts = listOfNotNull(
        itemData?.brand?.trim(),
        itemData?.type?.trim(),
        itemData?.variant?.trim()
    )
    return if (parts.isNotEmpty()) {
        parts.joinToString(" ")
    } else {
        name
    }
}
