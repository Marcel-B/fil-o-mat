package com.filomat.app

import com.filomat.app.data.Container
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
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
    fun itemIsFoundByEveryTagAndKeepsThemThroughSnapshot() {
        val snapshot = Snapshot(
            emptyList(),
            listOf(SnapshotItem("filofa", "04000001", "FiloFa", createdAt = 1, extraTags = listOf("04000002", "04000003")))
        )
        val state = StoreState.fromSnapshot(snapshot, emptyList())

        assertEquals("filofa", state.findItemByTag("04000003")?.id)
        assertNull(state.findItemByTag("04000009"))
        assertEquals(listOf("04000002", "04000003"), state.items.single().toSnapshot(sendWeights = false).extraTags)
        // Auch im Gerätespeicher bleiben die weiteren Tags erhalten
        assertEquals(listOf("04000001", "04000002", "04000003"),
            StoreState.fromStored(emptyList(), state.storedItems()).items.single().allTags)
    }

    @Test
    fun findsPossibleDuplicatesBySortAndColor() {
        val matte = Item(id = "a", rfidTag = "A", name = "A", itemData = ItemJsonData(type = "PLA", color_hex = "#161616", brand = "SUNLU", variant = "matte"))
        val same = Item(id = "b", rfidTag = "B", name = "B", itemData = ItemJsonData(type = "pla", color_hex = "161616", brand = "Sunlu ", variant = "Matte"))
        val white = Item(id = "c", rfidTag = "C", name = "C", itemData = ItemJsonData(type = "PLA", color_hex = "FFFFFF", brand = "SUNLU", variant = "matte"))
        val unnamed = Item(id = "d", rfidTag = "D", name = "D")
        val state = StoreState(items = listOf(matte, same, white, unnamed))

        assertEquals(listOf("b"), state.possibleDuplicates(matte).map { it.id })
        assertEquals(emptyList<Item>(), state.possibleDuplicates(white))
        assertEquals(emptyList<Item>(), state.possibleDuplicates(unnamed))
    }

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
    fun transmissionDistanceAndSpoolContainerSurviveSyncAndStorage() {
        val snapshot = Json { ignoreUnknownKeys = true }.decodeFromString<Snapshot>(
            """{"containers":[],"items":[{"id":"1","rfidTag":"T","name":"PLA","createdAt":2,"transmissionDistance":3.5}],
               "spools":[{"id":"s","itemId":"1","number":1,"containerId":"box","openedAt":5}]}"""
        )
        val state = StoreState.fromSnapshot(snapshot, emptyList())
        assertEquals("box", state.spoolsIn("box").single().containerId)
        // Der TD-Wert gehört nicht zu den Gewichten und geht immer mit
        assertEquals(3.5, state.items.single().toSnapshot(sendWeights = false).transmissionDistance)
        val stored = StoreState.fromStored(state.storedContainers(), state.storedItems())
        assertEquals(3.5, stored.items.single().transmissionDistance)
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
        assertEquals("P1S Slot 5", Printer("b", "P1S", "Bambu", "http://p1s", tools = 5).placeName(4))
        assertEquals("Lager", Spool("s", "i", 1).locationText(listOf(u1)))
        assertEquals("U1 Kopf 4", Spool("s", "i", 1, printerId = "p", tool = 3).locationText(listOf(u1)))
    }

    @Test
    fun spoolCountOfPrinterContainerIncludesLoadedSpools() {
        val kobra = Printer("k", "Kobra", "Anycubic", "http://kobra", containerId = "kc", tools = 8)
        val state = StoreState(
            items = listOf(
                Item(id = "a", rfidTag = "A", name = "PLA", containerId = "box"),
                Item(id = "b", rfidTag = "B", name = "PETG", containerId = "kc")
            ),
            spools = listOf(
                Spool("s1", "a", 1, printerId = "k", tool = 0),
                Spool("s2", "a", 2, printerId = "k", tool = 1),
                Spool("s3", "b", 1, printerId = "k", tool = 2),
                Spool("s4", "a", 3, containerId = "box")
            ),
            printers = listOf(kobra)
        )

        // Drei Spulen in Slots, eine in der Box; Sorten zählen nicht mit
        assertEquals(3, state.spoolCount("kc"))
        assertEquals(1, state.spoolCount("box"))
    }

    @Test
    fun looseShowsSpoolsWithoutKnownContainer() {
        val state = StoreState(
            containers = listOf(Container(id = "standard_lose", rfidTag = "L", name = "Lose"), Container(id = "box", rfidTag = "B", name = "Box")),
            spools = listOf(
                Spool("s1", "a", 1, containerId = "box"),
                Spool("s2", "a", 2),
                Spool("s3", "a", 3, containerId = "geloescht"),
                Spool("s4", "a", 4, printerId = "k", tool = 0)
            )
        )

        assertEquals(listOf("s1"), state.spoolsIn("box").map { it.id })
        assertEquals(listOf("s2", "s3"), state.spoolsIn("standard_lose").map { it.id })
    }
}
