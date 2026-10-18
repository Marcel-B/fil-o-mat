package com.filomat.app

import com.filomat.app.data.Item
import com.filomat.app.data.sync.ContainerData
import com.filomat.app.data.sync.ItemData
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.Snapshot
import com.filomat.app.data.sync.SnapshotContainer
import com.filomat.app.data.sync.SnapshotItem
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.StoreState
import com.filomat.app.data.sync.toSnapshot
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class StoreStateTest {
    private fun item(id: String, containerId: String? = null) =
        ItemData(id = id, rfidTag = "TAG$id", name = "Item $id", createdAt = 1, containerId = containerId)

    @Test
    fun legacyAssignmentFromContainerListIsKept() {
        val box = ContainerData("box", "B", "Box", createdAt = 1, items = listOf(item("1"), item("2")))
        // Item 1 kennt seinen Container nur über die Liste, Item 2 steht nur in der Liste
        val state = StoreState.fromStored(listOf(box), listOf(item("1"), item("3", "box")))

        assertEquals(listOf("1", "3", "2"), state.items.map { it.id })
        assertEquals(listOf("box", "box", "box"), state.items.map { it.containerId })
        assertEquals(3, state.containersWithItems().single().items.size)
    }

    @Test
    fun storedContainersCarryTheirItemsForOlderVersions() {
        val state = StoreState.fromStored(
            listOf(ContainerData("box", "B", "Box", createdAt = 1)),
            listOf(item("1", "box"), item("2"))
        )
        assertEquals(listOf("1"), state.storedContainers().single().items.map { it.id })
    }

    @Test
    fun oldStoredJsonStillLoads() {
        // So hat die App vor dieser Version gespeichert (Json ohne encodeDefaults)
        val containers = """[{"id":"standard_lose","rfidTag":"STANDARD_LOSE","name":"Lose","description":"","createdAt":5,"items":[],"isStandard":true}]"""
        val items = """[{"id":"1","rfidTag":"04AB","name":"Elegoo PLA","description":"","createdAt":7,"containerId":"standard_lose","itemData":{"type":"PLA","color_hex":"FF0000"}}]"""
        val json = Json { ignoreUnknownKeys = true }
        val state = StoreState.fromStored(json.decodeFromString(containers), json.decodeFromString<List<ItemData>>(items))
        assertEquals("PLA", state.items.single().itemData?.type)
        assertNull(state.items.single().netWeight)
        assertEquals(true, state.containers.single().isStandard)
    }

    @Test
    fun uploadLeavesOutStandardContainersAndWeights() {
        val state = StoreState(
            containers = StoreState.fromStored(
                listOf(
                    ContainerData("standard_lose", "STANDARD_LOSE", "Lose", createdAt = 1, isStandard = true),
                    ContainerData("box", "B", "Box", createdAt = 1)
                ), emptyList()
            ).containers,
            items = listOf(Item(id = "1", rfidTag = "T", name = "PLA", netWeight = 1000.0, spoolsInStock = 3))
        )
        val upload = state.uploadSnapshot()
        assertEquals(listOf("box"), upload.containers.map { it.id })
        assertNull(upload.items.single().netWeight)
        assertNull(upload.items.single().spoolsInStock)
        assertEquals(1000.0, state.items.single().toSnapshot(sendWeights = true).netWeight)
    }

    @Test
    fun snapshotIsTakenOver() {
        val snapshot = Snapshot(
            containers = listOf(SnapshotContainer("box", "B", "Box", createdAt = 1)),
            items = listOf(SnapshotItem("1", "T", "PLA", createdAt = 2, containerId = "box", spoolWeight = 180.0)),
            spools = listOf(Spool("s2", "1", 2), Spool("s1", "1", 1, 512.0, "p", 1))
        )
        val state = StoreState.fromSnapshot(snapshot, emptyList())
        assertEquals(180.0, state.items.single().spoolWeight)
        assertEquals(listOf(1, 2), state.spoolsOf("1").map { it.number })
    }

    @Test
    fun placeNames() {
        val u1 = Printer("p", "U1", "SnapmakerU1", "http://u1", tools = 4)
        val prusa = Printer("q", "Core One", "PrusaLink", "http://prusa", tools = 5)
        val mini = Printer("r", "Mini", "PrusaLink", "http://mini", tools = 1)
        assertEquals("U1 Kopf 2", u1.placeName(1))
        assertEquals("Core One Slot 3", prusa.placeName(2))
        assertEquals("Mini", mini.placeName(0))
        assertEquals("Lager", Spool("s", "i", 1).locationText(listOf(u1)))
        assertEquals("U1 Kopf 4", Spool("s", "i", 1, printerId = "p", tool = 3).locationText(listOf(u1)))
    }
}
