package com.filomat.app.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.net.HttpURLConnection
import java.net.URL

/** Container im Austauschformat des Backends (`POST /api/import`). */
@Serializable
data class SnapshotContainer(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String,
    val capacity: Int? = null,
    val createdAt: Long,
    val isStandard: Boolean = false
)

/** Filament im Austauschformat des Backends. */
@Serializable
data class SnapshotItem(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String,
    val createdAt: Long,
    val containerId: String?,
    val itemData: ItemJsonData? = null
)

@Serializable
data class Snapshot(val containers: List<SnapshotContainer>, val items: List<SnapshotItem>)

@Serializable
data class ImportResult(val containers: Int, val items: Int, val skipped: List<String> = emptyList())

/** Verbindung zum Fil-o-mat-Backend; Adresse und Schlüssel werden auf dem Gerät gespeichert. */
class BackendSync(context: Context) {
    private val prefs = context.getSharedPreferences("backend", Context.MODE_PRIVATE)
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    var serverUrl: String
        get() = prefs.getString("server_url", "") ?: ""
        set(value) = prefs.edit().putString("server_url", value.trim()).apply()

    var apiKey: String
        get() = prefs.getString("api_key", "") ?: ""
        set(value) = prefs.edit().putString("api_key", value.trim()).apply()

    /** Lädt alle Container und Filamente ins Backend. Vorhandene Einträge werden aktualisiert, nichts wird gelöscht. */
    suspend fun upload(store: ContainerStore): ImportResult = withContext(Dispatchers.IO) {
        val body = json.encodeToString(store.snapshot())
        val connection = URL("${serverUrl.trimEnd('/')}/api/import").openConnection() as HttpURLConnection
        try {
            connection.requestMethod = "POST"
            connection.connectTimeout = 10_000
            connection.readTimeout = 30_000
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            if (apiKey.isNotEmpty()) connection.setRequestProperty("X-Api-Key", apiKey)
            connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }

            val status = connection.responseCode
            if (status !in 200..299) {
                val error = connection.errorStream?.bufferedReader()?.use { it.readText() }.orEmpty()
                throw IllegalStateException(
                    if (status == 401) "Server lehnt den API-Schlüssel ab (401)." else "Server antwortet mit $status. $error".trim()
                )
            }
            json.decodeFromString<ImportResult>(connection.inputStream.bufferedReader().use { it.readText() })
        } finally {
            connection.disconnect()
        }
    }
}
