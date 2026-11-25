package com.filomat.app

import com.filomat.app.data.sync.ApiCompatibility
import com.filomat.app.data.sync.BackendVersion
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ApiCompatibilityTest {
    @Test
    fun comparesApiLevels() {
        val current = BackendVersion(apiLevel = 1, minAppLevel = 1, commit = "abc")
        assertEquals(ApiCompatibility.COMPATIBLE, ApiCompatibility.of(current, appLevel = 1, minBackendLevel = 1))
        // Backend ohne /api/version
        assertEquals(ApiCompatibility.BACKEND_OUTDATED, ApiCompatibility.of(null, appLevel = 1, minBackendLevel = 1))
        assertEquals(ApiCompatibility.BACKEND_OUTDATED, ApiCompatibility.of(current, appLevel = 2, minBackendLevel = 2))
        // Neueres Backend reicht der App, solange es sie noch unterstützt
        assertEquals(ApiCompatibility.COMPATIBLE, ApiCompatibility.of(BackendVersion(3, 1), appLevel = 1, minBackendLevel = 1))
        assertEquals(ApiCompatibility.APP_OUTDATED, ApiCompatibility.of(BackendVersion(3, 2), appLevel = 1, minBackendLevel = 1))
        assertNull(ApiCompatibility.COMPATIBLE.message)
    }
}
