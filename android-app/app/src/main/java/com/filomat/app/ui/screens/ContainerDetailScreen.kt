package com.filomat.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.graphics.Color
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Remove
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.ui.viewmodel.ContainerDetailViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContainerDetailScreen(
    containerId: String,
    navController: androidx.navigation.NavController,
    viewModel: ContainerDetailViewModel = viewModel()
) {
    val context = LocalContext.current
    val containerStore = remember { ContainerStore(context) }
    val container by viewModel.container.collectAsState()
    var showingEditContainer by remember { mutableStateOf(false) }
    var showingAddItem by remember { mutableStateOf(false) }
    var showingDeleteDialog by remember { mutableStateOf(false) }
    var itemToRemove by remember { mutableStateOf<com.filomat.app.data.Item?>(null) }
    var showingRemoveItemDialog by remember { mutableStateOf(false) }
    
    LaunchedEffect(containerId) {
        containerStore.containers.collect { containerList ->
            val found = containerList.firstOrNull { it.id == containerId }
            if (found != null) {
                viewModel.updateContainer(found)
            }
        }
    }
    
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(container?.name ?: "Container") },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Zurück")
                    }
                },
                actions = {
                    IconButton(onClick = { showingEditContainer = true }) {
                        Icon(Icons.Default.Edit, contentDescription = "Bearbeiten")
                    }
                    // Standard-Container können nicht gelöscht werden
                    if (container?.isStandard != true) {
                        IconButton(onClick = { showingDeleteDialog = true }) {
                            Icon(Icons.Default.Delete, contentDescription = "Löschen")
                        }
                    }
                }
            )
        },
        floatingActionButton = {
            FloatingActionButton(onClick = { showingAddItem = true }) {
                Icon(Icons.Default.Add, contentDescription = "Filament hinzufügen")
            }
        }
    ) { padding ->
        container?.let { cont ->
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                item {
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column(
                            modifier = Modifier.padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Text(
                                text = "Informationen",
                                style = MaterialTheme.typography.titleMedium
                            )
                            InfoRow("Name", cont.name)
                            InfoRow("RFID-Tag", cont.rfidTag)
                            if (cont.description.isNotEmpty()) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween
                                ) {
                                    Text(
                                        text = "Beschreibung",
                                        style = MaterialTheme.typography.bodyMedium,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                    Text(
                                        text = cont.description,
                                        style = MaterialTheme.typography.bodyMedium,
                                        modifier = Modifier.weight(1f)
                                    )
                                }
                            }
                        }
                    }
                }
                
                item {
                    Text(
                        text = "Filamente (${cont.items.size})",
                        style = MaterialTheme.typography.titleMedium
                    )
                }
                
                if (cont.items.isEmpty()) {
                    item {
                        Text(
                            text = "Keine Filamente in diesem Container",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                } else {
                    items(cont.items) { item ->
                        ItemCard(
                            item = item,
                            onRemove = {
                                itemToRemove = item
                                showingRemoveItemDialog = true
                            },
                            isLoseContainer = cont.id == "standard_lose"
                        )
                    }
                }
            }
        } ?: run {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = androidx.compose.ui.Alignment.Center
            ) {
                Text("Container nicht gefunden")
            }
        }
        
        if (showingEditContainer && container != null) {
            EditContainerDialog(
                container = container!!,
                onDismiss = { showingEditContainer = false },
                onSave = { updated ->
                    viewModel.updateContainer(updated, containerStore)
                    showingEditContainer = false
                }
            )
        }
        
        if (showingAddItem && container != null) {
            AddItemDialog(
                containerId = container!!.id,
                onDismiss = { showingAddItem = false },
                onAdd = { }, // Wird nicht mehr benötigt, da automatisch hinzugefügt wird
                containerStore = containerStore
            )
        }
        
        if (showingDeleteDialog && container != null) {
            AlertDialog(
                onDismissRequest = { showingDeleteDialog = false },
                title = { Text("Container löschen") },
                text = { 
                    Text("Möchten Sie den Container '${container!!.name}' wirklich löschen? Die Items bleiben erhalten, sind aber keinem Container mehr zugeordnet.")
                },
                confirmButton = {
                    TextButton(
                        onClick = {
                            viewModel.removeContainer(container!!.id, containerStore)
                            showingDeleteDialog = false
                            navController.popBackStack()
                        }
                    ) {
                        Text("Löschen", color = MaterialTheme.colorScheme.error)
                    }
                },
                dismissButton = {
                    TextButton(onClick = { showingDeleteDialog = false }) {
                        Text("Abbrechen")
                    }
                }
            )
        }
        
        // Dialog für Item-Entfernen
        if (showingRemoveItemDialog && itemToRemove != null && container != null) {
            if (container!!.id == "standard_lose") {
                // Von "Lose" aus direkt löschen
                AlertDialog(
                    onDismissRequest = {
                        showingRemoveItemDialog = false
                        itemToRemove = null
                    },
                    title = { Text("Filament löschen") },
                    text = {
                        Text("Möchten Sie das Filament '${itemToRemove!!.getDisplayTitle()}' wirklich komplett löschen?")
                    },
                    confirmButton = {
                        TextButton(
                            onClick = {
                                viewModel.removeItemCompletely(itemToRemove!!.id, containerStore)
                                showingRemoveItemDialog = false
                                itemToRemove = null
                            }
                        ) {
                            Text("Löschen", color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = {
                            showingRemoveItemDialog = false
                            itemToRemove = null
                        }) {
                            Text("Abbrechen")
                        }
                    }
                )
            } else {
                // Von normalem Container: Auswahl zwischen Löschen und Verschieben
                AlertDialog(
                    onDismissRequest = {
                        showingRemoveItemDialog = false
                        itemToRemove = null
                    },
                    title = { Text("Filament entfernen") },
                    text = {
                        Column(
                            modifier = Modifier.fillMaxWidth(),
                            verticalArrangement = Arrangement.spacedBy(16.dp)
                        ) {
                            Text("Was möchten Sie mit dem Filament '${itemToRemove!!.getDisplayTitle()}' machen?")
                            TextButton(
                                onClick = {
                                    viewModel.moveItemToLose(itemToRemove!!.id, container!!.id, containerStore)
                                    showingRemoveItemDialog = false
                                    itemToRemove = null
                                },
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Text("In 'Lose' verschieben")
                            }
                            TextButton(
                                onClick = {
                                    viewModel.removeItemCompletely(itemToRemove!!.id, containerStore)
                                    showingRemoveItemDialog = false
                                    itemToRemove = null
                                },
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Text("Komplett löschen", color = MaterialTheme.colorScheme.error)
                            }
                        }
                    },
                    confirmButton = {
                        TextButton(onClick = {
                            showingRemoveItemDialog = false
                            itemToRemove = null
                        }) {
                            Text("Abbrechen")
                        }
                    },
                    dismissButton = null
                )
            }
        }
    }
}

@Composable
fun InfoRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Text(
            text = value,
            style = MaterialTheme.typography.bodyMedium
        )
    }
}

@Composable
fun ItemCard(
    item: com.filomat.app.data.Item,
    onRemove: () -> Unit,
    isLoseContainer: Boolean = false
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = item.getDisplayTitle(),
                        style = MaterialTheme.typography.titleSmall
                    )
                    Text(
                        text = "RFID: ${item.rfidTag}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                IconButton(onClick = onRemove) {
                    Icon(
                        Icons.Default.Remove,
                        contentDescription = if (isLoseContainer) "Löschen" else "Entfernen",
                        tint = MaterialTheme.colorScheme.error
                    )
                }
            }
            if (item.description.isNotEmpty()) {
                Text(
                    text = item.description,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            
            // Zeige Item-Daten an, falls vorhanden
            item.itemData?.let { data ->
                Divider(modifier = Modifier.padding(vertical = 4.dp))
                Text(
                    text = "Tag-Daten:",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary
                )
                if (data.brand != null) {
                    InfoRow("Marke", data.brand)
                }
                if (data.type != null) {
                    InfoRow("Typ", data.type)
                }
                // Farbe immer anzeigen (mit Kreis oder n/a)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Farbe",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        data.color_hex?.let { colorHex ->
                            val trimmedHex = colorHex.trim()
                            if (trimmedHex.isNotEmpty()) {
                                val hexString = if (trimmedHex.startsWith("#")) {
                                    trimmedHex
                                } else {
                                    "#$trimmedHex"
                                }
                                val colorValue = try {
                                    val hexWithoutHash = hexString.substring(1)
                                    if (hexWithoutHash.length == 6 || hexWithoutHash.length == 8) {
                                        android.graphics.Color.parseColor(hexString)
                                    } else {
                                        null
                                    }
                                } catch (e: Exception) {
                                    null
                                }
                                if (colorValue != null) {
                                    Box(
                                        modifier = Modifier
                                            .size(20.dp)
                                            .background(
                                                color = androidx.compose.ui.graphics.Color(colorValue),
                                                shape = androidx.compose.foundation.shape.CircleShape
                                            )
                                            .border(
                                                width = 1.dp,
                                                color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                                                shape = androidx.compose.foundation.shape.CircleShape
                                            )
                                    )
                                }
                                Text(
                                    text = data.color?.trim()?.takeIf { it.isNotEmpty() } ?: hexString,
                                    style = MaterialTheme.typography.bodyMedium
                                )
                            } else {
                                Text(
                                    text = "n/a",
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        } ?: run {
                            Text(
                                text = "n/a",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
                if (data.min_temp != null && data.max_temp != null) {
                    InfoRow("Temperatur", "${data.min_temp}°C - ${data.max_temp}°C")
                }
                if (data.bed_min_temp != null && data.bed_max_temp != null) {
                    InfoRow("Bett-Temp", "${data.bed_min_temp}°C - ${data.bed_max_temp}°C")
                }
            }
        }
    }
}
