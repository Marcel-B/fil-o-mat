package com.filomat.app

import com.filomat.app.data.Container
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
import com.filomat.app.data.PlaceAction
import com.filomat.app.data.PlaceFlow
import com.filomat.app.data.PlaceStep
import com.filomat.app.data.PlaceTarget
import com.filomat.app.data.SpoolChoice
import com.filomat.app.data.Stock
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.StoreState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PlaceFlowTest {
    private val shelf = Container(id = "shelf", rfidTag = "SHELF", name = "Regal oben")
    private val u1Box = Container(id = "u1box", rfidTag = "U1BOX", name = "U1")
    private val u1 = Printer(id = "u1", name = "U1", type = "SnapmakerU1", baseUrl = "http://u1", containerId = "u1box", tools = 4)
    private val pla = Item(id = "pla", rfidTag = "PLA", name = "PLA", spoolsInStock = 2, netWeight = 1000.0)
    private val spool1 = Spool(id = "s1", itemId = "pla", number = 1, remainingWeight = 300.0, printerId = "u1", tool = 1)
    private val other = Spool(id = "s9", itemId = "petg", number = 9, remainingWeight = 800.0, printerId = "u1", tool = 2)
    private val state = StoreState(listOf(shelf, u1Box), listOf(pla), listOf(spool1, other), listOf(u1))

    @Test
    fun storingTheStockGoesToTheScannedContainer() {
        val scanned = PlaceFlow(PlaceTarget.CONTAINER).withFilament("PLA", null, state, connected = true)
        assertEquals(PlaceStep.CHOOSE_SPOOL, scanned.step)
        assertEquals(listOf(SpoolChoice.Stock, SpoolChoice.Opened("s1"), SpoolChoice.NewSpool), scanned.choices(state, true))
        assertEquals(listOf(SpoolChoice.Stock, SpoolChoice.Opened("s1")), scanned.choices(state, false))

        val flow = scanned.choose(SpoolChoice.Stock, state).withTargetTag("SHELF", state)
        assertEquals(PlaceAction.MoveItem(pla, shelf), flow.action(state))
    }

    @Test
    fun storingCanOpenANewSpoolIntoTheContainer() {
        val flow = PlaceFlow(PlaceTarget.CONTAINER).withFilament("PLA", null, state, true)
            .choose(SpoolChoice.NewSpool, state)
            .withTargetTag("SHELF", state)
        assertEquals("Anbrechen und einlagern", flow.actionTitle)
        assertEquals(PlaceAction.OpenSpool(pla, SpoolPlace.container("shelf")), flow.action(state))
    }

    @Test
    fun unknownPlateContinuesWithTheChosenSort() {
        val scanned = PlaceFlow(PlaceTarget.CONTAINER).withFilament("PLATE2", null, state, false)
        val withTag = state.copy(items = listOf(pla.copy(extraTags = listOf("PLATE2"))))
        val flow = scanned.withItem(withTag.item("pla")!!, withTag, false)
        assertNull(flow.unknownTag)
        assertEquals(PlaceStep.CHOOSE_SPOOL, flow.step)
    }

    @Test
    fun printerContainerIsRefusedForStoring() {
        val flow = PlaceFlow(PlaceTarget.CONTAINER).withFilament("PLA", null, state, true)
            .choose(SpoolChoice.Stock, state)
            .withTargetTag("U1BOX", state)
        assertNull(flow.containerId)
        assertNotNull(flow.error)
        assertEquals(listOf(shelf), state.storageContainers())
    }

    @Test
    fun unknownFilamentIsCreatedAndGoesStraightToTheTarget() {
        val data = ItemJsonData(brand = "Sunlu", type = "PETG")
        val scanned = PlaceFlow(PlaceTarget.CONTAINER).withFilament("NEU", data, state, false)
        // Unbekanntes Plättchen: erst fragen, nichts still anlegen
        assertEquals("NEU", scanned.unknownTag)
        assertNull(scanned.item)
        val (flow, created) = scanned.withNewItem(state, false)
        assertEquals("Sunlu PETG", created?.name)
        // Ohne angebrochene Spulen gibt es nur die Sorte, also kein Auswahlschritt
        assertEquals(PlaceStep.CHOOSE_TARGET, flow.step)
        assertEquals(SpoolChoice.Stock, flow.choice)
    }

    @Test
    fun printerFlowPreselectsTheCurrentHeadAndNamesTheDisplacedSpool() {
        var flow = PlaceFlow(PlaceTarget.PRINTER).withFilament("PLA", null, state, true)
        assertEquals(listOf(SpoolChoice.Opened("s1"), SpoolChoice.NewSpool), flow.choices(state, true))
        flow = flow.choose(SpoolChoice.Opened("s1"), state)
        assertEquals(SpoolPlace.printer("u1", 1), flow.printerPlace)
        assertTrue(flow.isAlreadyThere(state))
        assertFalse(flow.canPerform(state))

        flow = flow.selectPlace(SpoolPlace.printer("u1", 2))
        assertEquals(other, flow.displacedSpool(state))
        assertEquals(PlaceAction.MoveSpool(pla, spool1, SpoolPlace.printer("u1", 2)), flow.action(state))
    }

    @Test
    fun printerFlowNeedsTheBackendAndScanningThePrinterPicksItsFirstHead() {
        assertEquals(emptyList<SpoolChoice>(), PlaceFlow(PlaceTarget.PRINTER).withFilament("PLA", null, state, false).choices(state, false))

        val flow = PlaceFlow(PlaceTarget.PRINTER).withFilament("PLA", null, state, true)
            .choose(SpoolChoice.NewSpool, state)
            .withTargetTag("U1BOX", state)
        assertEquals(PlaceAction.OpenSpool(pla, SpoolPlace.printer("u1", 0)), flow.action(state))
        assertEquals("Anbrechen und einlegen", flow.actionTitle)
    }

    @Test
    fun spoolPlaceTextsAndStock() {
        val stored = spool1.movedTo(SpoolPlace.container("shelf"))
        assertEquals("Regal oben", stored.locationText(state.printers, state.containers))
        assertEquals("U1 Kopf 2", spool1.locationText(state.printers, state.containers))
        assertEquals("Lager", spool1.movedTo(SpoolPlace.STORAGE).locationText(state.printers))
        assertNull(stored.tool)

        val entry = Stock.entries(state.items, state.spools).single()
        assertEquals(2300.0, entry.totalGrams!!, 0.0)
        assertFalse(entry.isLow())
        val empty = entry.copy(item = pla.copy(spoolsInStock = 0))
        assertFalse(empty.isLow())
        assertTrue(empty.copy(openedSpools = listOf(spool1.copy(remainingWeight = 100.0))).isLow())
        assertFalse(entry.copy(item = pla.copy(spoolsInStock = null)).isLow())
    }

    @Test
    fun transmissionText() {
        assertEquals("3,5 mm", pla.copy(transmissionDistance = 3.5).transmissionText())
        assertEquals("4 mm", pla.copy(transmissionDistance = 4.0).transmissionText())
        assertNull(pla.transmissionText())
    }
}
