package com.filomat.app

import com.filomat.app.data.sync.BackendClient
import com.filomat.app.data.sync.PrinterLive
import com.filomat.app.data.sync.PrinterStatus
import com.filomat.app.data.sync.StatsMonth
import com.filomat.app.data.sync.gramsText
import kotlinx.serialization.decodeFromString
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PrinterLiveTest {
    /** Antwort von `/api/printers/status`, wie sie das Backend liefert (gekürzt). */
    private val response = """
        [{"printerId":"p1","name":"Kobra X","type":"Anycubic","updatedAt":"2026-10-24T18:40:12.1234567Z",
          "status":{"state":"Printing","jobName":"benchy","jobKey":"614707220","progress":45.5,"remainingSeconds":1920,
            "elapsedSeconds":600,"layer":12,"totalLayers":80,"activeTool":0,
            "heaters":[{"name":"Düse","tool":0,"actual":214.6,"target":215},{"name":"Bett","tool":null,"actual":59.8,"target":60}],
            "slots":[{"tool":0,"loaded":true,"type":"PLA","colorHex":"000000","remainPercent":null,"tagUid":null}],
            "dryers":[{"name":"ACE 1","humidityPercent":52,"humidityLevel":null,"temperature":31,"tools":[0,1,2,3]}],
            "jobGramsPerTool":[40,0],"jobGramsEstimated":true,"message":null,"hasThumbnail":false,"isActive":true},
          "tools":[{"tool":0,"place":"Slot 1","spoolId":"s1","spoolNumber":2,"itemId":"i1","itemName":"PLA Schwarz","type":"PLA",
                    "colorHex":"000000","remainingGrams":12,"reported":{"tool":0,"loaded":true,"type":"PLA","colorHex":"000000"},
                    "neededGrams":21.8,"enough":false},
                   {"tool":1,"place":"Slot 2","spoolId":null,"spoolNumber":null,"itemId":null,"itemName":null,"type":null,
                    "colorHex":null,"remainingGrams":null,"reported":{"tool":1,"loaded":false},"neededGrams":null,"enough":null}],
          "hints":[{"kind":"SpoolShort","warning":true,"tool":0,"text":"Slot 1: PLA Schwarz #2 hat noch 12 g."},
                   {"kind":"Humidity","warning":false,"tool":null,"text":"ACE 1: 52 % Feuchte."}]}]
    """.trimIndent()

    @Test
    fun decodesTheBackendStatus() {
        val live = BackendClient.json.decodeFromString<List<PrinterLive>>(response).single()
        assertNotNull(live.updatedDate)
        assertTrue(live.status.isActive)
        assertEquals("Druckt benchy · 45 % · noch 32 min", live.status.summary)
        assertEquals("215 °C / 215 °C", live.status.heaters[0].text)
        assertEquals("52 % Feuchte · 31 °C", live.status.dryers[0].text)
        assertEquals("PLA Schwarz #2", live.tools[0].title)
        assertEquals("leer", live.tools[1].title)
        assertFalse(live.tools[0].enough!!)
        assertEquals(1, live.warnings.size)
    }

    @Test
    fun formatsDurationsWeightsAndMonths() {
        assertEquals("1:23 h", PrinterStatus.duration(4980))
        assertEquals("1 min", PrinterStatus.duration(30))
        assertEquals("340 g", gramsText(340.4))
        assertEquals("1,5 kg", gramsText(1500.0))
        assertEquals("10/26", StatsMonth("2026-10").label(withYear = true))
    }
}
