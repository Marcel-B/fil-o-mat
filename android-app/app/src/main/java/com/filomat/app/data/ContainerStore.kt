package com.filomat.app.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json

private val Context.dataStore by preferencesDataStore(name = "container_store")

@Serializable
data class ContainerData(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String,
    val capacity: Int? = null,
    val createdAt: Long,
    val items: List<ItemData>,
    val isStandard: Boolean = false
)

@Serializable
data class ItemData(
    val id: String,
    val rfidTag: String,
    val name: String,
    val description: String,
    val createdAt: Long,
    val containerId: String?,
    val itemData: com.filomat.app.data.ItemJsonData? = null
)

class ContainerStore(private val context: Context) {
    private val containersKey = stringPreferencesKey("containers")
    private val itemsKey = stringPreferencesKey("items")
    private val standardContainersInitializedKey = stringPreferencesKey("standard_containers_initialized")
    
    init {
        // Initialisiere Standard-Container beim ersten Start
        initializeStandardContainers()
    }
    
    private fun initializeStandardContainers() {
        CoroutineScope(Dispatchers.IO).launch {
            val initialized = context.dataStore.data.first()[standardContainersInitializedKey] == "true"
            if (!initialized) {
                val standardContainers = listOf(
                    Container(
                        id = "standard_drucker",
                        rfidTag = "STANDARD_DRUCKER",
                        name = "Drucker",
                        description = "Standard-Container für Drucker",
                        isStandard = true
                    ),
                    Container(
                        id = "standard_lose",
                        rfidTag = "STANDARD_LOSE",
                        name = "Lose",
                        description = "Standard-Container für lose Items",
                        isStandard = true
                    )
                )
                
                context.dataStore.edit { preferences ->
                    val currentContainers = getContainersSync(preferences[containersKey], preferences[itemsKey])
                    val allContainers = currentContainers + standardContainers
                    preferences[containersKey] = Json.encodeToString(allContainers.map { it.toData() })
                    preferences[standardContainersInitializedKey] = "true"
                }
            }
        }
    }
    
    val containers: Flow<List<Container>> = context.dataStore.data.map { preferences ->
        val containersJson = preferences[containersKey] ?: "[]"
        val itemsJson = preferences[itemsKey] ?: "[]"
        try {
            // Verwende getContainersSync mit Items-Liste, um immer aktuelle itemData zu haben
            getContainersSync(containersJson, itemsJson)
        } catch (e: Exception) {
            emptyList()
        }
    }
    
    val allItems: Flow<List<Item>> = context.dataStore.data.map { preferences ->
        val itemsJson = preferences[itemsKey] ?: "[]"
        try {
            Json.decodeFromString<List<ItemData>>(itemsJson)
                .map { data ->
                    Item(
                        id = data.id,
                        rfidTag = data.rfidTag,
                        name = data.name,
                        description = data.description,
                        createdAt = data.createdAt,
                        containerId = data.containerId
                    )
                }
        } catch (e: Exception) {
            emptyList()
        }
    }
    
    suspend fun addContainer(container: Container) {
        context.dataStore.edit { preferences ->
            val currentContainers = getContainersSync(preferences[containersKey])
            val updated = currentContainers + container
            preferences[containersKey] = Json.encodeToString(updated.map { it.toData() })
        }
    }
    
    suspend fun updateContainer(container: Container) {
        context.dataStore.edit { preferences ->
            val currentContainers = getContainersSync(preferences[containersKey])
            val updated = currentContainers.map { if (it.id == container.id) container else it }
            preferences[containersKey] = Json.encodeToString(updated.map { it.toData() })
        }
    }
    
    suspend fun removeContainer(containerId: String) {
        context.dataStore.edit { preferences ->
            val currentContainers = getContainersSync(preferences[containersKey])
            val container = currentContainers.firstOrNull { it.id == containerId }
            
            // Standard-Container können nicht gelöscht werden
            if (container != null && container.isStandard) {
                return@edit
            }
            
            // Finde den "Lose" Container
            val loseContainer = currentContainers.firstOrNull { it.id == "standard_lose" }
            
            if (container != null && loseContainer != null) {
                val currentItems = getItemsSync(preferences[itemsKey])
                
                // Verschiebe alle Items in den "Lose" Container
                val updatedItems = currentItems.map { item ->
                    if (item.containerId == containerId) {
                        item.copy(containerId = loseContainer.id)
                    } else {
                        item
                    }
                }
                preferences[itemsKey] = Json.encodeToString(updatedItems.map { it.toData() })
                
                // Aktualisiere Container: Items zum "Lose" Container hinzufügen und aus gelöschtem Container entfernen
                val updatedContainers = currentContainers.map { cont ->
                    when {
                        cont.id == loseContainer.id -> {
                            // Füge alle Items vom gelöschten Container hinzu
                            val itemsToAdd = container.items.map { it.copy(containerId = loseContainer.id) }
                            cont.copy(items = (cont.items + itemsToAdd).toMutableList())
                        }
                        cont.id == containerId -> {
                            // Entferne alle Items aus dem zu löschenden Container
                            cont.copy(items = mutableListOf())
                        }
                        else -> cont
                    }
                }
                
                // Entferne den Container aus der Liste
                val finalContainers = updatedContainers.filter { it.id != containerId }
                preferences[containersKey] = Json.encodeToString(finalContainers.map { it.toData() })
            } else if (container != null) {
                // Wenn "Lose" Container nicht gefunden wurde, entferne Container ohne Items zu verschieben
                val finalContainers = currentContainers.filter { it.id != containerId }
                preferences[containersKey] = Json.encodeToString(finalContainers.map { it.toData() })
            }
        }
    }
    
    suspend fun findContainerByTag(rfidTag: String): Container? {
        val containersJson = context.dataStore.data.first()[containersKey]
        val containers = getContainersSync(containersJson)
        return containers.firstOrNull { it.rfidTag == rfidTag }
    }
    
    suspend fun addItem(item: Item) {
        context.dataStore.edit { preferences ->
            val currentItems = getItemsSync(preferences[itemsKey])
            // Prüfe ob Item mit diesem RFID-Tag bereits existiert
            if (currentItems.any { it.rfidTag == item.rfidTag && it.rfidTag.isNotBlank() }) {
                return@edit // Item existiert bereits, nicht erneut hinzufügen
            }
            val updated = currentItems + item
            preferences[itemsKey] = Json.encodeToString(updated.map { it.toData() })
            
            // Wenn containerId gesetzt ist, füge Item auch zum Container hinzu
            if (item.containerId != null) {
                val currentContainers = getContainersSync(preferences[containersKey])
                val updatedContainers = currentContainers.map { container ->
                    if (container.id == item.containerId && !container.items.any { it.id == item.id }) {
                        container.copy(items = (container.items + item).toMutableList())
                    } else {
                        container
                    }
                }
                preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
            }
        }
    }
    
    suspend fun updateItem(item: Item) {
        context.dataStore.edit { preferences ->
            val currentItems = getItemsSync(preferences[itemsKey])
            // Stelle sicher, dass itemData erhalten bleibt
            val updated = currentItems.map { 
                if (it.id == item.id) {
                    // Verwende das übergebene Item, aber stelle sicher, dass itemData kopiert wird
                    item.copy(itemData = item.itemData)
                } else {
                    it
                }
            }
            preferences[itemsKey] = Json.encodeToString(updated.map { it.toData() })
            
            // Aktualisiere auch in Containern - verwende immer das aktualisierte Item aus der Items-Liste
            val updatedItemFromList = updated.firstOrNull { it.id == item.id } ?: item
            val currentContainers = getContainersSync(preferences[containersKey])
            val updatedContainers = currentContainers.map { container ->
                val updatedItems = container.items.map { 
                    if (it.id == item.id) {
                        // Verwende das aktualisierte Item aus der Items-Liste (mit allen Daten)
                        updatedItemFromList.copy(itemData = updatedItemFromList.itemData)
                    } else {
                        it
                    }
                }
                container.copy(items = updatedItems.toMutableList())
            }
            preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
        }
    }
    
    suspend fun removeItem(itemId: String) {
        context.dataStore.edit { preferences ->
            val currentItems = getItemsSync(preferences[itemsKey])
            val updated = currentItems.filter { it.id != itemId }
            preferences[itemsKey] = Json.encodeToString(updated.map { it.toData() })
            
            // Entferne auch aus Containern
            val currentContainers = getContainersSync(preferences[containersKey])
            val updatedContainers = currentContainers.map { container ->
                container.copy(items = container.items.filter { it.id != itemId }.toMutableList())
            }
            preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
        }
    }
    
    suspend fun findItemByTag(rfidTag: String): Item? {
        val itemsJson = context.dataStore.data.first()[itemsKey]
        val items = getItemsSync(itemsJson)
        return items.firstOrNull { it.rfidTag == rfidTag }
    }
    
    suspend fun addItemToContainer(itemId: String, containerId: String) {
        context.dataStore.edit { preferences ->
            // Hole immer die neuesten Items aus dem Store
            val currentItems = getItemsSync(preferences[itemsKey])
            val item = currentItems.firstOrNull { it.id == itemId } ?: return@edit
            
            // Stelle sicher, dass ALLE Item-Daten erhalten bleiben (inkl. itemData)
            // Verwende copy() um sicherzustellen, dass itemData mit kopiert wird
            val updatedItem = item.copy(
                containerId = containerId,
                itemData = item.itemData // Explizit itemData kopieren
            )
            
            // Aktualisiere Item in der Items-Liste ZUERST
            val updatedItems = currentItems.map { if (it.id == itemId) updatedItem else it }
            preferences[itemsKey] = Json.encodeToString(updatedItems.map { it.toData() })
            
            // Hole Container - verwende Items aus der zentralen Liste für aktuelle Daten
            val currentContainers = getContainersSync(preferences[containersKey], preferences[itemsKey])
            val updatedContainers = currentContainers.map { container ->
                if (container.id == containerId) {
                    // Entferne Item falls es bereits im Container ist (für Verschieben)
                    val itemsWithoutItem = container.items.filter { it.id != itemId }
                    // Verwende IMMER das aktualisierte Item aus der Items-Liste (mit allen Daten)
                    container.copy(items = (itemsWithoutItem + updatedItem).toMutableList())
                } else {
                    // Entferne Item aus anderen Containern
                    container.copy(items = container.items.filter { it.id != itemId }.toMutableList())
                }
            }
            preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
        }
    }
    
    // Atomare Funktion zum Verschieben eines Items von einem Container in einen anderen
    suspend fun moveItemToContainer(itemId: String, fromContainerId: String?, toContainerId: String) {
        context.dataStore.edit { preferences ->
            // Hole immer die neuesten Items aus dem Store
            val currentItems = getItemsSync(preferences[itemsKey])
            val item = currentItems.firstOrNull { it.id == itemId } ?: return@edit
            
            // Stelle sicher, dass ALLE Item-Daten erhalten bleiben (inkl. itemData)
            // Verwende copy() um sicherzustellen, dass itemData mit kopiert wird
            val updatedItem = item.copy(
                containerId = toContainerId,
                itemData = item.itemData // Explizit itemData kopieren
            )
            
            // Aktualisiere Item in der Items-Liste ZUERST
            val updatedItems = currentItems.map { if (it.id == itemId) updatedItem else it }
            preferences[itemsKey] = Json.encodeToString(updatedItems.map { it.toData() })
            
            // Hole Container - verwende Items aus der zentralen Liste für aktuelle Daten
            val currentContainers = getContainersSync(preferences[containersKey], preferences[itemsKey])
            val updatedContainers = currentContainers.map { container ->
                when {
                    container.id == toContainerId -> {
                        // Entferne Item falls es bereits im Container ist
                        val itemsWithoutItem = container.items.filter { it.id != itemId }
                        // Verwende IMMER das aktualisierte Item aus der Items-Liste (mit allen Daten)
                        container.copy(items = (itemsWithoutItem + updatedItem).toMutableList())
                    }
                    container.id == fromContainerId -> {
                        // Entferne Item aus dem Quell-Container
                        container.copy(items = container.items.filter { it.id != itemId }.toMutableList())
                    }
                    else -> {
                        // Entferne Item aus allen anderen Containern (falls es dort versehentlich ist)
                        container.copy(items = container.items.filter { it.id != itemId }.toMutableList())
                    }
                }
            }
            preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
        }
    }
    
    suspend fun removeItemFromContainer(itemId: String, containerId: String) {
        context.dataStore.edit { preferences ->
            val currentItems = getItemsSync(preferences[itemsKey])
            val item = currentItems.firstOrNull { it.id == itemId }
            if (item != null) {
                val updatedItem = item.copy(containerId = null)
                val updatedItems = currentItems.map { if (it.id == itemId) updatedItem else it }
                preferences[itemsKey] = Json.encodeToString(updatedItems.map { it.toData() })
            }
            
            val currentContainers = getContainersSync(preferences[containersKey])
            val updatedContainers = currentContainers.map { container ->
                if (container.id == containerId) {
                    container.copy(items = container.items.filter { it.id != itemId }.toMutableList())
                } else {
                    container
                }
            }
            preferences[containersKey] = Json.encodeToString(updatedContainers.map { it.toData() })
        }
    }
    
    private fun getContainersSync(json: String?, itemsJson: String? = null): List<Container> {
        if (json.isNullOrEmpty()) return emptyList()
        return try {
            val containerDataList = Json.decodeFromString<List<ContainerData>>(json)
            // Hole Items aus der zentralen Items-Liste, falls verfügbar
            val allItems = if (itemsJson != null) {
                getItemsSync(itemsJson)
            } else {
                emptyList()
            }
            
            containerDataList.map { data ->
                Container(
                    id = data.id,
                    rfidTag = data.rfidTag,
                    name = data.name,
                    description = data.description,
                    capacity = data.capacity,
                    createdAt = data.createdAt,
                    isStandard = data.isStandard,
                    items = data.items.map { itemData ->
                        // Verwende Item aus zentraler Liste, falls verfügbar (hat immer neueste Daten)
                        val itemFromCentralList = allItems.firstOrNull { it.id == itemData.id }
                        if (itemFromCentralList != null) {
                            // Verwende Item aus zentraler Liste (hat immer neueste itemData)
                            itemFromCentralList
                        } else {
                            // Fallback: Erstelle Item aus ContainerData (kann veraltete Daten haben)
                            Item(
                                id = itemData.id,
                                rfidTag = itemData.rfidTag,
                                name = itemData.name,
                                description = itemData.description,
                                createdAt = itemData.createdAt,
                                containerId = itemData.containerId,
                                itemData = itemData.itemData
                            )
                        }
                    }.toMutableList()
                )
            }
        } catch (e: Exception) {
            emptyList()
        }
    }
    
    private fun getItemsSync(json: String?): List<Item> {
        if (json.isNullOrEmpty()) return emptyList()
        return try {
            Json.decodeFromString<List<ItemData>>(json).map { data ->
                Item(
                    id = data.id,
                    rfidTag = data.rfidTag,
                    name = data.name,
                    description = data.description,
                    createdAt = data.createdAt,
                    containerId = data.containerId,
                    itemData = data.itemData // WICHTIG: itemData muss geladen werden!
                )
            }
        } catch (e: Exception) {
            emptyList()
        }
    }
    
    private fun Container.toData() = ContainerData(
        id = id,
        rfidTag = rfidTag,
        name = name,
        description = description,
        capacity = capacity,
        createdAt = createdAt,
        isStandard = isStandard,
        items = items.map { it.toData() }
    )
    
    private fun Item.toData() = ItemData(
        id = id,
        rfidTag = rfidTag,
        name = name,
        description = description,
        createdAt = createdAt,
        containerId = containerId,
        itemData = itemData
    )
}
