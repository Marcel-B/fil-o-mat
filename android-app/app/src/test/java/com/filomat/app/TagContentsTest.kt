package com.filomat.app

import com.filomat.app.nfc.TagContents
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class TagContentsTest {
    @Test
    fun readsOpenSpoolWithNumbersAndExtraFields() {
        val contents = TagContents.parse(
            """{"protocol":"openspool","version":"1.0","type":"PETG","brand":"Elegoo","subtype":"Rapid","color_hex":"1A2B3C","min_temp":230,"max_temp":250,"weight":1000,"vendor_id":"x"}"""
        )
        assertNull(contents.error)
        assertEquals("230", contents.itemData?.min_temp)
        assertEquals("Rapid", contents.itemData?.variant)
        assertEquals(listOf("brand", "type", "subtype", "color_hex", "min_temp", "max_temp", "weight", "protocol", "version", "vendor_id"),
            contents.fields.map { it.key })
        assertEquals("Gewicht (g)", contents.fields.first { it.key == "weight" }.label)
        assertEquals("vendor_id", contents.fields.last().label)
    }

    @Test
    fun reportsBrokenJson() {
        val contents = TagContents.parse("{nope")
        assertNotNull(contents.error)
        assertNull(contents.itemData)
        assertEquals("{nope", contents.rawJson)
    }
}
