package com.filomat.app

import com.filomat.app.data.Container
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolFilter
import com.filomat.app.data.sync.StoreState
import com.filomat.app.data.sync.spoolEntries
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class SpoolListTest {
    private val state = StoreState(
        containers = listOf(Container(id = "box", rfidTag = "X", name = "Regal oben")),
        items = listOf(
            Item(id = "pla", rfidTag = "A", name = "PLA", itemData = ItemJsonData(type = "PLA", brand = "Prusament", color = "Galaxy Black"), netWeight = 1000.0),
            Item(id = "petg", rfidTag = "B", name = "PETG", itemData = ItemJsonData(type = "PETG", brand = "Sunlu"), netWeight = 1000.0)
        ),
        spools = listOf(
            Spool(id = "s2", itemId = "pla", number = 2, remainingWeight = 50.0, printerId = "u1", tool = 1),
            Spool(id = "s1", itemId = "pla", number = 1, containerId = "box"),
            Spool(id = "s3", itemId = "petg", number = 1, remainingWeight = 800.0)
        ),
        printers = listOf(Printer(id = "u1", name = "U1", type = "SnapmakerU1", baseUrl = "", containerId = "c-u1", tools = 4))
    )

    @Test
    fun sortsBySortThenNumberAndNamesThePlace() {
        val all = state.spoolEntries()
        assertEquals(listOf("s1", "s2", "s3"), all.map { it.spool.id })
        assertEquals(listOf("Regal oben", "U1 Kopf 2", "Lager"), all.map { it.place })
    }

    @Test
    fun unweighedSpoolCountsAsFull() {
        val first = state.spoolEntries().first()
        assertFalse(first.isWeighed)
        assertEquals(1000.0, first.remaining!!, 0.0)
        assertEquals(0.05, state.spoolEntries()[1].fraction!!, 0.0001)
    }

    @Test
    fun filtersByPrinterOrStorage() {
        assertEquals(listOf("s2"), state.spoolEntries(filter = SpoolFilter.PRINTER).map { it.spool.id })
        assertEquals(listOf("s1", "s3"), state.spoolEntries(filter = SpoolFilter.STORAGE).map { it.spool.id })
    }

    @Test
    fun searchesSortColorPlaceAndNumber() {
        assertEquals(listOf("s1", "s2"), state.spoolEntries("galaxy").map { it.spool.id })
        assertEquals(listOf("s1"), state.spoolEntries("regal").map { it.spool.id })
        assertEquals(listOf("s2"), state.spoolEntries("#2").map { it.spool.id })
    }
}
