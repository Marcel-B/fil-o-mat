package com.filomat.app

import com.filomat.app.data.sync.BackendClient
import com.filomat.app.data.sync.BackendException
import com.filomat.app.data.sync.Snapshot
import com.filomat.app.data.sync.SnapshotItem
import com.filomat.app.data.sync.SpoolUpdate
import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test
import java.net.InetSocketAddress

class BackendClientTest {
    private lateinit var server: HttpServer
    private val requests = mutableListOf<Triple<String, String, String>>()
    private val keys = mutableListOf<String?>()
    private val responses = mutableMapOf<String, Pair<Int, String>>()

    @Before
    fun start() {
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/") { exchange ->
            val path = exchange.requestURI.toString()
            requests += Triple(exchange.requestMethod, path, exchange.requestBody.readBytes().decodeToString())
            keys += exchange.requestHeaders.getFirst("X-Api-Key")
            val (status, body) = responses["${exchange.requestMethod} $path"] ?: (404 to "")
            val bytes = body.toByteArray()
            exchange.sendResponseHeaders(status, if (bytes.isEmpty()) -1 else bytes.size.toLong())
            if (bytes.isNotEmpty()) exchange.responseBody.use { it.write(bytes) }
            exchange.close()
        }
        server.start()
    }

    @After
    fun stop() = server.stop(0)

    private fun client(key: String = "geheim") = BackendClient("http://127.0.0.1:${server.address.port}/", key)

    @Test
    fun readsSnapshotWithNullsAndSpools() = runBlocking {
        responses["GET /api/snapshot"] = 200 to """{"containers":[{"id":"standard_lose","rfidTag":"STANDARD_LOSE","name":"Lose","description":null,"capacity":null,"createdAt":1,"isStandard":true}],
            "items":[{"id":"1","rfidTag":"04AB","name":"PLA","description":null,"createdAt":2,"containerId":null,"itemData":{"type":"PLA"},"netWeight":1000,"spoolWeight":null,"spoolsInStock":2}],
            "spools":[{"id":"s","itemId":"1","number":1,"remainingWeight":432.5,"printerId":"p","tool":2,"openedAt":3}]}"""
        val snapshot = client().snapshot()
        assertEquals("", snapshot.containers.single().description)
        assertEquals(1000.0, snapshot.items.single().netWeight)
        assertEquals(2, snapshot.spools!!.single().tool)
        assertEquals("geheim", keys.single())
    }

    @Test
    fun sendsImportAndSpoolRequests() = runBlocking {
        responses["POST /api/import"] = 200 to """{"containers":0,"items":1,"skipped":["x"]}"""
        responses["POST /api/items/a%20b/spools"] = 201 to """{"id":"s","itemId":"a b","number":4,"remainingWeight":null,"printerId":null,"tool":null,"openedAt":"2026-10-18T22:00:00Z","emptiedAt":null}"""
        val result = client().importData(Snapshot(emptyList(), listOf(SnapshotItem("1", "T", "PLA", createdAt = 1))))
        assertEquals(listOf("x"), result.skipped)
        assertTrue(requests[0].third.contains("\"netWeight\":null"))
        assertEquals(4, client().openSpool("a b", SpoolUpdate()).number)
    }

    @Test
    fun deleteToleratesMissingEntries() = runBlocking {
        client().deleteItem("weg")
        assertEquals("DELETE", requests.single().first)
    }

    @Test
    fun rejectsWrongKey() = runBlocking {
        responses["GET /api/printers"] = 401 to ""
        try {
            client("falsch").printers()
            fail()
        } catch (e: BackendException) {
            assertEquals(401, e.status)
        }
    }

    @Test
    fun readsUpdateStatus() = runBlocking {
        responses["GET /api/update"] = 200 to """{"supported":true,"branch":"main","currentVersion":"abc","currentDate":5,"latestVersion":"def","updateAvailable":true,"commits":[{"sha":"def","subject":"Neu","date":6}],"checkedAt":7,"state":"running","message":null}"""
        val status = client().updateStatus()
        assertTrue(status.isUpdating)
        assertEquals("Neu", status.commits.single().subject)
        assertNull(status.message)
    }

    @Test
    fun validatesAddresses() {
        assertNull(BackendClient.create("nas.local:8080", ""))
        assertNull(BackendClient.create("ftp://nas", ""))
        assertEquals("http://nas.local:8080", BackendClient.create(" http://nas.local:8080/ ", " ")!!.baseUrl)
    }
}
