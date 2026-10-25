package com.filomat.app.data

import kotlinx.serialization.Serializable
import java.util.UUID

data class Item(
    val id: String = UUID.randomUUID().toString(),
    val rfidTag: String,
    var name: String,
    var description: String = "",
    val createdAt: Long = System.currentTimeMillis(),
    var containerId: String? = null,
    var itemData: ItemJsonData? = null,
    /** Filamentgewicht einer vollen Spule in Gramm; wird vom Backend gepflegt. */
    val netWeight: Double? = null,
    /** Gewicht der leeren Spule in Gramm, um eine gewogene Spule umzurechnen. */
    val spoolWeight: Double? = null,
    /** Volle Spulen dieser Sorte im Vorrat; `null`, wenn kein Vorrat geführt wird. */
    val spoolsInStock: Int? = null,
    /** Lichtdurchlässigkeit als Transmission Distance (TD, HueForge) in mm; steht nur in Fil-o-mat, nicht auf dem Tag. */
    val transmissionDistance: Double? = null,
    /** Weitere Tags derselben Sorte, z. B. Musterplättchen an Boxen und Druckern; `null` bei älteren Daten. */
    val extraTags: List<String>? = null
) {
    /** Haupt-Tag und weitere Tags. */
    val allTags: List<String> get() = (listOf(rfidTag) + extraTags.orEmpty()).filter { it.isNotBlank() }

    fun hasTag(tag: String): Boolean = tag.isNotBlank() && tag in allTags

    fun getDisplayTitle(): String {
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

    /** "3,5 mm", falls die Lichtdurchlässigkeit gemessen ist. */
    fun transmissionText(): String? = transmissionDistance?.let {
        val rounded = Math.round(it * 100) / 100.0
        val text = if (rounded == Math.rint(rounded)) rounded.toLong().toString() else rounded.toString()
        "${text.replace('.', ',')} mm"
    }

    /** Anzeigename der Farbe: Klartext, sonst der Hex-Wert mit "#". */
    fun colorLabel(): String? {
        itemData?.color?.trim()?.takeIf { it.isNotEmpty() }?.let { return it }
        val hex = itemData?.color_hex?.trim()?.takeIf { it.isNotEmpty() } ?: return null
        return if (hex.startsWith("#")) hex else "#$hex"
    }
}

@Serializable
data class ItemJsonData(
    val protocol: String? = null,
    val version: String? = null,
    val type: String? = null,
    val color_hex: String? = null,
    val brand: String? = null,
    val min_temp: String? = null,
    val max_temp: String? = null,
    val bed_min_temp: String? = null,
    val bed_max_temp: String? = null,
    val variant: String? = null,
    val color: String? = null
)
