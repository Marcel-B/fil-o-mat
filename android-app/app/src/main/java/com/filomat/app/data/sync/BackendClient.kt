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

    /** Führt das Filament [fromId] in [itemId] zusammen; liefert die Spulen, die eine neue Nummer bekommen haben. */
    suspend fun mergeItem(itemId: String, fromId: String): List<RenumberedSpool> = io {
        decode<MergeResult>(send("POST", "api/items/${path(itemId)}/merge", json.encodeToString(MergeRequest(fromId)))).renumbered
    }

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

    suspend fun updatePrinter(id: String, request: PrinterUpdateRequest): Printer = io {
        decode(send("PUT", "api/printers/${path(id)}", json.encodeToString(request)))
    }

    suspend fun deletePrinter(id: String) = io { send("DELETE", "api/printers/${path(id)}", allowNotFound = true); Unit }

    suspend fun syncPrinter(id: String): SyncResponse = io { decode(send("POST", "api/printers/${path(id)}/sync")) }

    suspend fun consumption(itemId: String): List<ConsumptionEntry> = io {
        decode(send("GET", "api/consumption?itemId=${URLEncoder.encode(itemId, "UTF-8")}"))
    }

    /** Zustand aller Drucker mit Spulen pro Kopf und Hinweisen; das Backend fragt nach, was älter als 10 s ist. */
    suspend fun printerStatus(): List<PrinterLive> = io { decode(send("GET", "api/printers/status")) }

    /** Vorschaubild des laufenden Drucks; `null`, wenn der Drucker keins liefert. */
    suspend fun printerThumbnail(id: String): ByteArray? = io {
        // Bei Bambu lädt das Backend dafür die ganze Druckdatei
        sendBytes("GET", "api/printers/${path(id)}/thumbnail", readTimeout = 60_000)
    }

    suspend fun stats(months: Int): StatsResponse = io { decode(send("GET", "api/stats?months=$months")) }

    // Meldungen

    suspend fun notificationSettings(): NotificationSettings = io { decode(send("GET", "api/settings/notifications")) }

    suspend fun saveNotificationSettings(request: NotificationSettingsRequest): NotificationSettings = io {
        decode(send("PUT", "api/settings/notifications", json.encodeToString(request)))
    }

    suspend fun sendTestNotification() = io { send("POST", "api/settings/notifications/test"); Unit }

    // Aktualisierung des Backends

    /** `null` bei einem Backend ohne `/api/version`. */
    suspend fun version(): BackendVersion? = io {
        send("GET", "api/version", allowNotFound = true).takeIf { it.isNotEmpty() }?.let { decode<BackendVersion>(it) }
    }

    suspend fun updateStatus(): BackendUpdateStatus = io { decode(send("GET", "api/update")) }

    /** Lässt den Server sofort nach neuen Commits suchen; das Ergebnis liefert später `updateStatus()`. */
    suspend fun checkForUpdate() = io { send("POST", "api/update/check"); Unit }

    /** Spielt die neue Version ein; das Backend startet dabei neu. */
    suspend fun installUpdate() = io { send("POST", "api/update"); Unit }

    private suspend fun <T> io(block: () -> T): T = withContext(Dispatchers.IO) { block() }

    private inline fun <reified T> decode(text: String): T = json.decodeFromString(text)

    private fun path(id: String): String = URLEncoder.encode(id, "UTF-8").replace("+", "%20")

    private fun send(method: String, path: String, body: String? = null, allowNotFound: Boolean = false): String =
        sendBytes(method, path, body, allowNotFound)?.toString(Charsets.UTF_8) ?: ""

    /** Antwort als Bytes; `null` bei 404 mit [allowNotFound]. */
    private fun sendBytes(
        method: String,
        path: String,
        body: String? = null,
        allowNotFound: Boolean = true,
        readTimeout: Int = 30_000
    ): ByteArray? {
        val connection = URL("$baseUrl/$path").openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 10_000
            connection.readTimeout = readTimeout
            connection.setRequestProperty("Accept", "application/json, image/*")
            if (!apiKey.isNullOrEmpty()) connection.setRequestProperty("X-Api-Key", apiKey)
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            if (status == 404 && allowNotFound) return null
            if (status == 401) throw BackendException("Der Server lehnt den API-Schlüssel ab.", status)
            if (status !in 200..299) {
                connection.errorStream?.close()
                throw BackendException("Der Server antwortet mit Fehler $status.", status)
            }
            return connection.inputStream.use { it.readBytes() }
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
