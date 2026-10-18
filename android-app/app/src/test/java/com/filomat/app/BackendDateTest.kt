package com.filomat.app

import com.filomat.app.data.sync.BackendDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BackendDateTest {
    @Test
    fun parsesDotNetTimestamps() {
        assertEquals(1_792_361_473_123L, BackendDate.parse("2026-10-18T22:11:13.1234567Z")?.time)
        assertEquals(1_792_361_473_000L, BackendDate.parse("2026-10-18T22:11:13Z")?.time)
        assertEquals(1_792_361_473_500L, BackendDate.parse("2026-10-18T22:11:13.5")?.time)
        assertEquals(1_792_361_473_000L, BackendDate.parse("2026-10-19T00:11:13+02:00")?.time)
        assertNull(BackendDate.parse("gestern"))
    }
}
