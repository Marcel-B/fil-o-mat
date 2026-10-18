package com.filomat.app.data.sync

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

class BackendException(message: String, val status: Int? = null) : IOException(message)

/** Zugriff auf das Fil-o-mat-Backend (wie `BackendClient` der iOS-App). */
class BackendClient(baseUrl: String, private val apiKey: String?) {
    val baseUrl: String = baseUrl.trim().trimEnd('/')

    suspend fun snapshot(): Snapshot = io { decode(send("GET", "api/snapshot")) }

    /** Legt neue Einträge an und aktualisiert vorhandene; löscht nichts. */
    suspend fun importData(snapshot: Snapshot): ImportResult = io {
        decode(send("POST", "api/import", json.encodeToString(snapshot)))
    }

    suspend fun deleteContainer(id: String) = io { send("DELETE", "api/containers/${path(id)}", allowNotFound = true); Unit }

    suspend fun deleteItem(id: String) = io { send("DELETE", "api/items/${path(id)}", allowNotFound = true); Unit }

    /** Leergewichte von Spulen pro Hersteller (aus der SpoolmanDB, über das Backend). */
    suspend fun spoolWeights(): Map<String, Double> = io { decode(send("GET", "api/spool-weights")) }

    // Spulen

    /** Neue Spule der Sorte anbrechen; ohne Restgewicht ist sie voll, der Vorrat sinkt um eins. */
    suspend fun openSpool(itemId: String, update: SpoolUpdate = SpoolUpdate()): Spool = io {
        decode(send("POST", "api/items/${path(itemId)}/spools", json.encodeToString(update)))
    }

    /** Restgewicht und Platz setzen; was am Zielplatz steckt, wandert ins Lager. */
    suspend fun updateSpool(id: String, update: SpoolUpdate): Spool = io {
        decode(send("PUT", "api/spools/${path(id)}", json.encodeToString(update)))
    }

    suspend fun markSpoolEmpty(id: String) = io { send("POST", "api/spools/${path(id)}/empty"); Unit }

    // Drucker

    suspend fun printers(): List<Printer> = io { decode(send("GET", "api/printers")) }

    suspend fun createPrinter(request: PrinterRequest): Printer = io {
        decode(send("POST", "api/printers", json.encodeToString(request)))
    }

    suspend fun deletePrinter(id: String) = io { send("DELETE", "api/printers/${path(id)}", allowNotFound = true); Unit }

    suspend fun syncPrinter(id: String): SyncResponse = io { decode(send("POST", "api/printers/${path(id)}/sync")) }

    suspend fun consumption(itemId: String): List<ConsumptionEntry> = io {
        decode(send("GET", "api/consumption?itemId=${URLEncoder.encode(itemId, "UTF-8")}"))
    }

    // Aktualisierung des Backends

    suspend fun updateStatus(): BackendUpdateStatus = io { decode(send("GET", "api/update")) }

    /** Lässt den Server sofort nach neuen Commits suchen; das Ergebnis liefert später `updateStatus()`. */
    suspend fun checkForUpdate() = io { send("POST", "api/update/check"); Unit }

    /** Spielt die neue Version ein; das Backend startet dabei neu. */
    suspend fun installUpdate() = io { send("POST", "api/update"); Unit }

    private suspend fun <T> io(block: () -> T): T = withContext(Dispatchers.IO) { block() }

    private inline fun <reified T> decode(text: String): T = json.decodeFromString(text)

    private fun path(id: String): String = URLEncoder.encode(id, "UTF-8").replace("+", "%20")

    private fun send(method: String, path: String, body: String? = null, allowNotFound: Boolean = false): String {
        val connection = URL("$baseUrl/$path").openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 10_000
            connection.readTimeout = 30_000
            connection.setRequestProperty("Accept", "application/json")
            if (!apiKey.isNullOrEmpty()) connection.setRequestProperty("X-Api-Key", apiKey)
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            if (status == 404 && allowNotFound) return ""
            if (status == 401) throw BackendException("Der Server lehnt den API-Schlüssel ab.", status)
            if (status !in 200..299) {
                connection.errorStream?.close()
                throw BackendException("Der Server antwortet mit Fehler $status.", status)
            }
            return connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
        } finally {
            connection.disconnect()
        }
    }

    companion object {
        val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
            // Das Backend schickt `null` z. B. für eine leere Beschreibung
            coerceInputValues = true
            explicitNulls = true
        }

        /** Gültige Server-Adresse (http oder https mit Host) oder `null`. */
        fun create(serverUrl: String, apiKey: String): BackendClient? {
            val trimmed = serverUrl.trim()
            val url = try { URL(trimmed) } catch (e: Exception) { return null }
            if (url.protocol != "http" && url.protocol != "https" || url.host.isNullOrEmpty()) return null
            return BackendClient(trimmed, apiKey.trim().ifEmpty { null })
        }
    }
}
