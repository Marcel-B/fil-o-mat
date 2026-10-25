package com.filomat.app.data

import android.content.Context
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.filomat.app.data.sync.BackendClient
import com.filomat.app.data.sync.BackendUpdateStatus
import com.filomat.app.data.sync.ConsumptionEntry
import com.filomat.app.data.sync.ContainerData
import com.filomat.app.data.sync.ImportResult
import com.filomat.app.data.sync.ItemData
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.PrinterLive
import com.filomat.app.data.sync.Snapshot
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.SpoolUpdate
import com.filomat.app.data.sync.StoreState
import com.filomat.app.data.sync.toSnapshot
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

private val Context.dataStore by preferencesDataStore(name = "container_store")

/** Zustand des Abgleichs mit dem Backend, für die Oberfläche. */
data class SyncState(
    val connected: Boolean = false,
    val isSyncing: Boolean = false,
    val error: String? = null,
    val lastSyncedAt: Long? = null,
    /** Stand der Backend-Version; `null`, solange unbekannt oder ohne Updater auf dem Server. */
    val updateStatus: BackendUpdateStatus? = null
)

/**
 * Lokale Datenhaltung für Container und Filamente (DataStore, dieselben Schlüssel wie bisher, damit die
 * vorhandenen Daten bleiben).
 *
 * Ist ein Backend verbunden, wird jede Änderung dorthin übertragen und `refresh()` übernimmt den Stand
 * des Backends, wie in der iOS-App.
 */
class ContainerStore private constructor(context: Context) {
    private val appContext = context.applicationContext
    private val dataStore = appContext.dataStore
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val syncMutex = Mutex()
    private val prefs = appContext.getSharedPreferences("backend", Context.MODE_PRIVATE)

    private val containersKey = stringPreferencesKey("containers")
    private val itemsKey = stringPreferencesKey("items")
    private val spoolsKey = stringPreferencesKey("spools")
    private val printersKey = stringPreferencesKey("printers")
    private val lastSyncedKey = longPreferencesKey("last_synced_at")
    private val standardContainersInitializedKey = stringPreferencesKey("standard_containers_initialized")

    private val _sync = MutableStateFlow(SyncState())
    val sync: StateFlow<SyncState> = _sync.asStateFlow()

    private val _printerLive = MutableStateFlow<Map<String, PrinterLive>>(emptyMap())
    /** Zustand der Drucker pro Drucker-ID; leer bei einem Backend ohne `/api/printers/status`. */
    val printerLive: StateFlow<Map<String, PrinterLive>> = _printerLive.asStateFlow()

    /** Verbundenes Backend; `null`, solange nicht verbunden. */
    @Volatile
    var backend: BackendClient? = null
        private set

    val serverUrl: String get() = prefs.getString("server_url", "") ?: ""
    val apiKey: String get() = prefs.getString("api_key", "") ?: ""

    init {
        // Adresse und Schlüssel speichert die App schon länger; verbunden ist sie aber erst nach dem
        // ersten Abgleich, bei dem die eigenen Daten zuerst hochgeladen werden.
        if (prefs.getBoolean("connected", false)) {
            backend = BackendClient.create(serverUrl, apiKey)
        }
        _sync.value = SyncState(connected = backend != null)
        scope.launch {
            val lastSynced = dataStore.data.first()[lastSyncedKey]
            _sync.update { it.copy(lastSyncedAt = lastSynced) }
        }
        initializeStandardContainers()
    }

    val state: Flow<StoreState> = dataStore.data.map { decode(it) }

    val containers: Flow<List<Container>> = state.map { it.containersWithItems() }
    val allItems: Flow<List<Item>> = state.map { it.items }
    val spools: Flow<List<Spool>> = state.map { it.spools }
    val printers: Flow<List<Printer>> = state.map { it.printers }

    suspend fun current(): StoreState = state.first()

    // Container

    suspend fun addContainer(container: Container) {
        edit { it.copy(containers = it.containers + container) }
        push(containers = listOf(container))
    }

    suspend fun updateContainer(container: Container) {
        edit { s -> s.copy(containers = s.containers.map { if (it.id == container.id) container else it }) }
        push(containers = listOf(container))
    }

    /** Löscht einen Container. Standard-Container bleiben, enthaltene Filamente wandern nach "Lose". */
    suspend fun removeContainer(containerId: String) {
        var removed = false
        edit { s ->
            val container = s.container(containerId)
            if (container == null || container.isStandard) return@edit s
            removed = true
            val loose = s.container(LOOSE_ID)?.id
            s.copy(
                containers = s.containers.filter { it.id != containerId },
                items = s.items.map { if (it.containerId == containerId) it.copy(containerId = loose) else it }
            )
        }
        if (removed) remote { it.deleteContainer(containerId) }
    }

    suspend fun findContainerByTag(rfidTag: String): Container? =
        current().let { s -> s.containers.firstOrNull { it.rfidTag == rfidTag }?.let { c -> c.copy(items = s.itemsIn(c.id).toMutableList()) } }

    // Filamente

    /** Fügt ein Filament hinzu, sofern noch keins mit demselben (nicht leeren) Tag existiert. */
    suspend fun addItem(item: Item) {
        var added = false
        edit { s ->
            if (item.rfidTag.isNotBlank() && s.findItemByTag(item.rfidTag) != null) return@edit s
            added = true
            s.copy(items = s.items + item)
        }
        if (added) push(items = listOf(item))
    }

    /** @param sendWeights Gewichte und Vorrat mit ans Backend schicken (nur wenn sie bewusst geändert wurden). */
    suspend fun updateItem(item: Item, sendWeights: Boolean = false) {
        edit { s -> s.copy(items = s.items.map { if (it.id == item.id) item else it }) }
        push(items = listOf(item), sendWeights = sendWeights)
    }

    suspend fun removeItem(itemId: String) {
        edit { s -> s.copy(items = s.items.filter { it.id != itemId }) }
        remote { it.deleteItem(itemId) }
    }

    suspend fun findItemByTag(rfidTag: String): Item? = current().findItemByTag(rfidTag)

    /**
     * Fügt einem Filament ein weiteres Tag hinzu, z. B. ein Musterplättchen an einer Box oder einem Drucker.
     * Gehört das Tag bisher einem anderen Filament, wird es dort entfernt; war es dessen einziges Tag,
     * wird der Eintrag gelöscht (nur ohne angebrochene Spulen).
     */
    suspend fun addTag(tag: String, itemId: String) {
        val state = current()
        val previous = state.findItemByTag(tag)
        if (previous != null && previous.id != itemId) {
            if (previous.allTags.size > 1) {
                removeTag(tag, previous.id)
            } else {
                check(state.spoolsOf(previous.id).isEmpty()) {
                    "Das Tag gehört zu „${previous.getDisplayTitle()}“, das angebrochene Spulen hat."
                }
                backend?.deleteItem(previous.id)
                edit { s -> s.copy(items = s.items.filter { it.id != previous.id }) }
            }
        }
        val item = current().item(itemId) ?: return
        if (item.hasTag(tag)) return
        saveTags(
            if (item.rfidTag.isBlank()) item.copy(rfidTag = tag)
            else item.copy(extraTags = item.extraTags.orEmpty() + tag)
        )
    }

    /** Entfernt ein Tag von einem Filament. Ist es das Haupt-Tag, rückt das erste weitere Tag nach; das letzte Tag bleibt. */
    suspend fun removeTag(tag: String, itemId: String) {
        val item = current().item(itemId) ?: return
        val extra = item.extraTags.orEmpty()
        saveTags(
            if (item.rfidTag == tag) {
                check(extra.isNotEmpty()) { "Das letzte Tag eines Filaments bleibt." }
                item.copy(rfidTag = extra.first(), extraTags = extra.drop(1))
            } else {
                item.copy(extraTags = extra - tag)
            }
        )
    }

    private suspend fun saveTags(item: Item) {
        edit { s -> s.copy(items = s.items.map { if (it.id == item.id) item else it }) }
        val client = backend ?: return
        val result = client.importData(Snapshot(emptyList(), listOf(item.toSnapshot(sendWeights = false))))
        result.skipped.firstOrNull()?.let { throw IllegalStateException(it) }
        refresh()
    }

    suspend fun addItemToContainer(itemId: String, containerId: String) = moveItem(itemId, containerId)

    @Suppress("UNUSED_PARAMETER")
    suspend fun moveItemToContainer(itemId: String, fromContainerId: String?, toContainerId: String) =
        moveItem(itemId, toContainerId)

    /** Nimmt ein Filament aus seinem Container heraus; danach ist es keinem Container zugeordnet. */
    @Suppress("UNUSED_PARAMETER")
    suspend fun removeItemFromContainer(itemId: String, containerId: String) {
        var moved: Item? = null
        edit { s ->
            s.copy(items = s.items.map { if (it.id == itemId) it.copy(containerId = null).also { m -> moved = m } else it })
        }
        moved?.let { push(items = listOf(it)) }
    }

    /** Lagert ein Filament in einen Container ein; es erscheint dort am Ende der Liste. */
    private suspend fun moveItem(itemId: String, containerId: String) {
        var moved: Item? = null
        edit { s ->
            val item = s.item(itemId) ?: return@edit s
            val updated = item.copy(containerId = containerId).also { moved = it }
            s.copy(items = s.items.filter { it.id != itemId } + updated)
        }
        moved?.let { push(items = listOf(it)) }
    }

    // Backend

    /**
     * Verbindet mit dem Backend: lädt zuerst die Daten dieses Geräts hoch (nichts wird gelöscht)
     * und übernimmt danach den gemeinsamen Stand.
     */
    suspend fun connect(serverUrl: String, apiKey: String): ImportResult? {
        val client = BackendClient.create(serverUrl, apiKey) ?: run {
            _sync.update { it.copy(error = "Ungültige Server-Adresse.") }
            return null
        }
        prefs.edit().putString("server_url", serverUrl.trim()).putString("api_key", apiKey.trim()).apply()
        return syncMutex.withLock {
            _sync.update { it.copy(isSyncing = true) }
            try {
                val result = client.importData(current().uploadSnapshot())
                applySnapshot(client.snapshot(), loadPrinters(client))
                backend = client
                prefs.edit().putBoolean("connected", true).apply()
                _sync.update { it.copy(connected = true, error = null) }
                result
            } catch (e: Exception) {
                _sync.update { it.copy(error = describe(e)) }
                null
            } finally {
                _sync.update { it.copy(isSyncing = false) }
            }
        }
    }

    fun disconnect() {
        backend = null
        prefs.edit().putBoolean("connected", false).remove("server_url").remove("api_key").apply()
        _sync.update { SyncState(lastSyncedAt = null) }
        _printerLive.value = emptyMap()
    }

    /** Zustand der Drucker; Fehler (z. B. ein älteres Backend) zählen nicht als Sync-Fehler. */
    suspend fun refreshPrinterLive() {
        val client = backend ?: return
        val live = runCatching { client.printerStatus() }.getOrNull() ?: return
        _printerLive.value = live.associateBy { it.printerId }
    }

    /** Übernimmt den Stand des Backends (z. B. Container, die auf einem anderen Gerät angelegt wurden). */
    suspend fun refresh() {
        val client = backend ?: return
        if (syncMutex.isLocked) return
        syncMutex.withLock {
            _sync.update { it.copy(isSyncing = true) }
            try {
                applySnapshot(client.snapshot(), loadPrinters(client))
                _sync.update { it.copy(error = null) }
                refreshPrinterLive()
            } catch (e: Exception) {
                _sync.update { it.copy(error = describe(e)) }
            } finally {
                _sync.update { it.copy(isSyncing = false) }
            }
        }
    }

    /** Fragt, ob es eine neue Backend-Version gibt. Fehler (z. B. ein Backend ohne Updater) zählen nicht als Sync-Fehler. */
    suspend fun refreshUpdateStatus() {
        val client = backend ?: return
        val status = try {
            client.updateStatus()
        } catch (e: Exception) {
            // Während des Neustarts antwortet das Backend kurz nicht; den Stand dann behalten
            _sync.update { if (it.updateStatus?.isUpdating == true) it else it.copy(updateStatus = null) }
            return
        }
        _sync.update { it.copy(updateStatus = status.takeIf { s -> s.supported }) }
    }

    suspend fun checkForUpdate() {
        backend?.checkForUpdate()
    }

    suspend fun installUpdate() {
        val client = backend ?: return
        client.installUpdate()
        refreshUpdateStatus()
    }

    // Spulen

    /**
     * Bricht eine volle Spule der Sorte an (aus dem Vorrat, Platz: Lager). Die eingetragenen Gewichte und der
     * Vorrat werden vorher gespeichert und abgewartet, damit der Vorrat danach nicht überschrieben wird.
     */
    suspend fun openSpool(item: Item) {
        val client = backend ?: return
        edit { s -> s.copy(items = s.items.map { if (it.id == item.id) item else it }) }
        client.importData(Snapshot(emptyList(), listOf(item.toSnapshot(sendWeights = true))))
        client.openSpool(item.id)
        refresh()
    }

    /** Bricht eine volle Spule der Sorte an und legt sie gleich an [place]; liefert die neue Spule (mit Nummer). */
    suspend fun openSpool(itemId: String, place: SpoolPlace): Spool? {
        val client = backend ?: return null
        val spool = client.openSpool(itemId, SpoolUpdate(printerId = place.printerId, tool = place.tool, containerId = place.containerId))
        refresh()
        return spool
    }

    suspend fun updateSpool(spool: Spool) {
        val client = backend ?: return
        client.updateSpool(spool.id, SpoolUpdate(spool.remainingWeight, spool.printerId, spool.tool, spool.containerId))
        refresh()
    }

    /** Legt eine angebrochene Spule an einen anderen Platz; was dort im Drucker steckte, wandert ins Lager. */
    suspend fun placeSpool(spool: Spool, place: SpoolPlace) = updateSpool(spool.movedTo(place))

    suspend fun markSpoolEmpty(spoolId: String) {
        val client = backend ?: return
        client.markSpoolEmpty(spoolId)
        refresh()
    }

    suspend fun consumption(itemId: String): List<ConsumptionEntry> =
        backend?.let { runCatching { it.consumption(itemId) }.getOrNull() } ?: emptyList()

    /** Leergewicht einer Spule für die Marke aus der SpoolmanDB (über das Backend). */
    suspend fun suggestedSpoolWeight(brand: String): Double? {
        val name = brand.trim().lowercase().ifEmpty { return null }
        val weights = backend?.let { runCatching { it.spoolWeights() }.getOrNull() } ?: return null
        return weights.entries.firstOrNull { it.key.lowercase() == name }?.value
            ?: weights.entries.firstOrNull { it.key.lowercase().startsWith(name) }?.value
    }

    /** Lädt die Drucker neu, z. B. nachdem einer angelegt oder entfernt wurde. */
    suspend fun reloadPrinters(): List<Printer> {
        val client = backend ?: return emptyList()
        val loaded = client.printers()
        dataStore.edit { it[printersKey] = json.encodeToString(loaded) }
        return loaded
    }

    private suspend fun loadPrinters(client: BackendClient): List<Printer>? =
        runCatching { client.printers() }.getOrNull()

    private suspend fun applySnapshot(snapshot: Snapshot, printers: List<Printer>?) {
        val now = System.currentTimeMillis()
        dataStore.edit { prefs ->
            val previous = decode(prefs)
            write(prefs, StoreState.fromSnapshot(snapshot, printers ?: previous.printers))
            prefs[standardContainersInitializedKey] = "true"
            prefs[lastSyncedKey] = now
        }
        _sync.update { it.copy(lastSyncedAt = now) }
    }

    private fun push(containers: List<Container> = emptyList(), items: List<Item> = emptyList(), sendWeights: Boolean = false) {
        val snapshot = Snapshot(containers.map { it.toSnapshot() }, items.map { it.toSnapshot(sendWeights) })
        remote { it.importData(snapshot) }
    }

    private fun remote(operation: suspend (BackendClient) -> Unit) {
        val client = backend ?: return
        scope.launch {
            try {
                operation(client)
                _sync.update { it.copy(error = null) }
            } catch (e: Exception) {
                _sync.update { it.copy(error = describe(e)) }
            }
        }
    }

    // Persistenz

    private suspend fun edit(transform: (StoreState) -> StoreState) {
        dataStore.edit { prefs -> write(prefs, transform(decode(prefs))) }
    }

    private fun decode(prefs: Preferences): StoreState {
        val containers = decodeList<ContainerData>(prefs[containersKey])
        val items = decodeList<ItemData>(prefs[itemsKey])
        return StoreState.fromStored(containers, items).copy(
            spools = decodeList(prefs[spoolsKey]),
            printers = decodeList(prefs[printersKey])
        )
    }

    private fun write(prefs: MutablePreferences, state: StoreState) {
        prefs[containersKey] = json.encodeToString(state.storedContainers())
        prefs[itemsKey] = json.encodeToString(state.storedItems())
        prefs[spoolsKey] = json.encodeToString(state.spools)
        prefs[printersKey] = json.encodeToString(state.printers)
    }

    private inline fun <reified T> decodeList(text: String?): List<T> =
        if (text.isNullOrEmpty()) emptyList()
        else try {
            json.decodeFromString<List<T>>(text)
        } catch (e: Exception) {
            android.util.Log.e("ContainerStore", "Gespeicherte Daten nicht lesbar: ${e.message}")
            emptyList()
        }

    private fun initializeStandardContainers() {
        scope.launch {
            dataStore.edit { prefs ->
                if (prefs[standardContainersInitializedKey] == "true") return@edit
                val state = decode(prefs)
                val missing = standardContainers.filter { s -> state.containers.none { it.id == s.id } }
                write(prefs, state.copy(containers = state.containers + missing))
                prefs[standardContainersInitializedKey] = "true"
            }
        }
    }

    companion object {
        const val PRINTER_ID = StoreState.PRINTER_CONTAINER_ID
        const val LOOSE_ID = "standard_lose"

        private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true; coerceInputValues = true }

        private val standardContainers = listOf(
            Container(id = PRINTER_ID, rfidTag = "STANDARD_DRUCKER", name = "Drucker",
                description = "Standard-Container für Drucker", isStandard = true),
            Container(id = LOOSE_ID, rfidTag = "STANDARD_LOSE", name = "Lose",
                description = "Standard-Container für lose Items", isStandard = true)
        )

        @Volatile
        private var instance: ContainerStore? = null

        fun get(context: Context): ContainerStore =
            instance ?: synchronized(this) { instance ?: ContainerStore(context).also { instance = it } }

        /** Fehlermeldung für die Anzeige; GrapheneOS sperrt das Netz pro App mit EPERM. */
        fun describe(e: Throwable): String {
            val message = e.message ?: e.javaClass.simpleName
            return when {
                message.contains("EPERM") ->
                    "Kein Netzwerkzugriff. In den App-Einstellungen von Fil-o-mat die Berechtigung „Netzwerk“ erlauben."
                e is java.net.UnknownHostException -> "Server nicht gefunden: $message"
                e is java.net.ConnectException || e is java.net.SocketTimeoutException -> "Server nicht erreichbar: $message"
                else -> message
            }
        }
    }
}
