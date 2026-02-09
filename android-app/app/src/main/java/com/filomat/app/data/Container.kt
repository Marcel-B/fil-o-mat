package com.filomat.app.data

import java.util.UUID

data class Container(
    val id: String = UUID.randomUUID().toString(),
    val rfidTag: String,
    var name: String,
    var description: String = "",
    var capacity: Int? = null,
    val createdAt: Long = System.currentTimeMillis(),
    val items: MutableList<Item> = mutableListOf(),
    val isStandard: Boolean = false
) {
    fun addItem(item: Item) {
        items.add(item)
    }
    
    fun removeItem(itemId: String) {
        items.removeAll { it.id == itemId }
    }
}
