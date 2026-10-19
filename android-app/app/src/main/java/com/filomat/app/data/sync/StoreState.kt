package com.filomat.app.data.sync

import com.filomat.app.data.Container
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
import kotlinx.serialization.Serializable

/** Container, wie er auf dem Gerät gespeichert wird. `items` schreibt die App nur noch für ältere Versionen mit. */
@Serializable
data class ContainerData(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String = "",
    val capacity: Int? = null,
    val createdAt: Long,
    val items: List<ItemData> = emptyList(),
    val isStandard: Boolean = false
)

/** Filament, wie es auf dem Gerät gespeichert wird. */
@Serializable
data class ItemData(
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
    val transmissionDistance: Double? = null
)

/**
 * Der gespeicherte Stand. Wie in der iOS-App gibt es nur eine Quelle für die Zuordnung: `Item.containerId`.
 * Die Item-Listen der Container werden daraus abgeleitet.
 */
data class StoreState(
    val containers: List<Container> = emptyList(),
    val items: List<Item> = emptyList(),
    val spools: List<Spool> = emptyList(),
    val printers: List<Printer> = emptyList()
) {
    fun container(id: String?): Container? = containers.firstOrNull { it.id == id }
    fun item(id: String): Item? = items.firstOrNull { it.id == id }
    fun itemsIn(containerId: String): List<Item> = items.filter { it.containerId == containerId }
    fun spoolsOf(itemId: String): List<Spool> = spools.filter { it.itemId == itemId }.sortedBy { it.number }
    fun spool(id: String): Spool? = spools.firstOrNull { it.id == id }
    fun findItemByTag(tag: String): Item? = items.firstOrNull { it.rfidTag == tag }
    fun findContainerByTag(tag: String): Container? = containers.firstOrNull { it.rfidTag == tag }

    /** Angebrochene Spulen, die im Lager in diesem Container liegen. */
    fun spoolsIn(containerId: String): List<Spool> =
        spools.filter { it.printerId == null && it.containerId == containerId }.sortedBy { it.number }

    /** Die Spule, die in diesem Kopf steckt. */
    fun spoolIn(printerId: String, tool: Int): Spool? =
        spools.firstOrNull { it.printerId == printerId && (it.tool ?: 0) == tool }

    /** Der Drucker, dem dieser Container gehört (jeder Drucker im Backend hat einen). */
    fun printerOwning(containerId: String): Printer? = printers.firstOrNull { it.containerId == containerId }

    /** Container, in die man Filament einlagern kann: alle außer denen der Drucker. */
    fun storageContainers(): List<Container> =
        containers.filter { it.id != PRINTER_CONTAINER_ID && printerOwning(it.id) == null }

    /** Container mit ihren Filamenten, so wie die Oberfläche sie anzeigt. */
    fun containersWithItems(): List<Container> =
        containers.map { it.copy(items = itemsIn(it.id).toMutableList()) }

    /** Die eigenen Daten für den ersten Abgleich; Standard-Container und Gewichte pflegt das Backend. */
    fun uploadSnapshot(): Snapshot = Snapshot(
        containers = containers.filter { !it.isStandard }.map { it.toSnapshot() },
        items = items.map { it.toSnapshot(sendWeights = false) }
    )

    companion object {
        /** Standard-Container "Drucker"; gehört zu keinem Drucker im Backend, ist aber kein Lager. */
        const val PRINTER_CONTAINER_ID = "standard_drucker"

        /**
         * Liest den gespeicherten Stand. Ältere Versionen der App kannten die Zuordnung teils nur über die
         * Item-Liste des Containers, und Filamente standen manchmal nur dort; beides wird übernommen.
         */
        fun fromStored(containers: List<ContainerData>, items: List<ItemData>): StoreState {
            val known = items.map { it.id }.toMutableSet()
            val merged = items.map { item ->
                if (item.containerId != null) item
                else item.copy(containerId = containers.firstOrNull { c -> c.items.any { it.id == item.id } }?.id)
            }.toMutableList()
            for (container in containers) {
                for (item in container.items) {
                    if (known.add(item.id)) merged.add(item.copy(containerId = container.id))
                }
            }
            return StoreState(
                containers = containers.map { it.toContainer() },
                items = merged.map { it.toItem() }
            )
        }

        fun fromSnapshot(snapshot: Snapshot, printers: List<Printer>): StoreState = StoreState(
            containers = snapshot.containers.map {
                Container(it.id, it.rfidTag, it.name, it.description, it.capacity, it.createdAt, isStandard = it.isStandard)
            },
            items = snapshot.items.map {
                Item(it.id, it.rfidTag, it.name, it.description, it.createdAt, it.containerId, it.itemData,
                    it.netWeight, it.spoolWeight, it.spoolsInStock, it.transmissionDistance)
            },
            spools = snapshot.spools ?: emptyList(),
            printers = printers
        )
    }

    fun storedContainers(): List<ContainerData> = containers.map { container ->
        ContainerData(
            id = container.id,
            rfidTag = container.rfidTag,
            name = container.name,
            description = container.description,
            capacity = container.capacity,
            createdAt = container.createdAt,
            items = itemsIn(container.id).map { it.toData() },
            isStandard = container.isStandard
        )
    }

    fun storedItems(): List<ItemData> = items.map { it.toData() }
}

fun Container.toSnapshot() = SnapshotContainer(id, rfidTag, name, description, capacity, createdAt, isStandard)

/**
 * Ohne `sendWeights` gehen keine Gewichte mit, damit gebuchter Verbrauch nicht mit einem älteren Stand überschrieben wird.
 * Die Lichtdurchlässigkeit geht immer mit; das Backend übernimmt sie nur, wenn sie gesetzt ist.
 */
fun Item.toSnapshot(sendWeights: Boolean) = SnapshotItem(
    id, rfidTag, name, description, createdAt, containerId, itemData,
    netWeight = if (sendWeights) netWeight else null,
    spoolWeight = if (sendWeights) spoolWeight else null,
    spoolsInStock = if (sendWeights) spoolsInStock else null,
    transmissionDistance = transmissionDistance
)

private fun ContainerData.toContainer() =
    Container(id, rfidTag, name, description, capacity, createdAt, isStandard = isStandard)

private fun ItemData.toItem() =
    Item(id, rfidTag, name, description, createdAt, containerId, itemData, netWeight, spoolWeight, spoolsInStock, transmissionDistance)

private fun Item.toData() =
    ItemData(id, rfidTag, name, description, createdAt, containerId, itemData, netWeight, spoolWeight, spoolsInStock, transmissionDistance)
