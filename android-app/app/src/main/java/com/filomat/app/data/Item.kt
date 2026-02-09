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
    var itemData: ItemJsonData? = null
) {
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
