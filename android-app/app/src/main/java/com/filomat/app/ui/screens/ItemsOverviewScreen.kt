package com.filomat.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import kotlinx.coroutines.launch
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import android.graphics.Color as AndroidColor
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ItemsOverviewScreen(
    navController: androidx.navigation.NavController
) {
    val context = LocalContext.current
    val containerStore = remember { ContainerStore(context) }
    var allItems by remember { mutableStateOf<List<Item>>(emptyList()) }
    var allContainers by remember { mutableStateOf<List<Container>>(emptyList()) }
    var searchQuery by remember { mutableStateOf("") }
    var itemToDelete by remember { mutableStateOf<Item?>(null) }
    var showingDeleteDialog by remember { mutableStateOf(false) }
    var itemToEdit by remember { mutableStateOf<Item?>(null) }
    var showingEditDialog by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    
    LaunchedEffect(Unit) {
        containerStore.containers.collect { containers ->
            allContainers = containers
            // Sammle alle Items aus allen Containern
            val items = containers.flatMap { it.items }
            allItems = items
        }
    }
    
    // Filtere und gruppiere Items
    val filteredAndGrouped = remember(allItems, searchQuery) {
        val filtered = if (searchQuery.isBlank()) {
            allItems
        } else {
            val query = searchQuery.lowercase()
            allItems.filter { item ->
                item.name.lowercase().contains(query) ||
                item.itemData?.type?.lowercase()?.contains(query) == true ||
                item.itemData?.brand?.lowercase()?.contains(query) == true ||
                item.rfidTag.lowercase().contains(query)
            }
        }
        
        // Gruppiere nach type
        filtered.groupBy { item ->
            item.itemData?.type ?: "Ohne Typ"
        }.toList().sortedBy { it.first }
    }
    
    var showingCreateFilament by remember { mutableStateOf(false) }
    
    Scaffold(
        topBar = {
            TopAppBar(title = { Text("Filamente") })
        },
        floatingActionButton = {
            FloatingActionButton(onClick = { showingCreateFilament = true }) {
                Icon(Icons.Default.Add, contentDescription = "Filament erstellen")
            }
        },
        bottomBar = {
            NavigationBar {
                NavigationBarItem(
                    selected = false,
                    onClick = { navController.navigate("containers") },
                    icon = { Icon(Icons.Default.Archive, contentDescription = null) },
                    label = { Text("Container") }
                )
                NavigationBarItem(
                    selected = false,
                    onClick = { navController.navigate("scan") },
                    icon = { Icon(Icons.Default.QrCodeScanner, contentDescription = null) },
                    label = { Text("Scannen") }
                )
                NavigationBarItem(
                    selected = true,
                    onClick = { },
                    icon = { Icon(Icons.Default.Search, contentDescription = null) },
                    label = { Text("Filamente") }
                )
            }
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            // Suchfeld
            OutlinedTextField(
                value = searchQuery,
                onValueChange = { searchQuery = it },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                placeholder = { Text("Suchen...") },
                leadingIcon = {
                    Icon(Icons.Default.Search, contentDescription = "Suchen")
                },
                singleLine = true
            )
            
            if (filteredAndGrouped.isEmpty()) {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = if (searchQuery.isBlank()) "Keine Filamente vorhanden" else "Keine Ergebnisse gefunden",
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    filteredAndGrouped.forEach { (type, items) ->
                        // Gruppen-Header
                        item {
                            Text(
                                text = "$type (${items.size})",
                                style = MaterialTheme.typography.titleMedium,
                                modifier = Modifier.padding(vertical = 8.dp),
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                        
                        // Items in dieser Gruppe
                        items(items) { item ->
                            ItemOverviewCard(
                                item = item,
                                containers = allContainers,
                                onDelete = {
                                    itemToDelete = item
                                    showingDeleteDialog = true
                                },
                                onEdit = {
                                    itemToEdit = item
                                    showingEditDialog = true
                                }
                            )
                        }
                    }
                }
            }
        }
    }
    
    // Dialog für neues Filament
    if (showingCreateFilament) {
        CreateFilamentDialog(
            onDismiss = { showingCreateFilament = false }
        )
    }
    
    // Bearbeiten-Dialog
    if (showingEditDialog && itemToEdit != null) {
        EditFilamentDialog(
            item = itemToEdit!!,
            containerStore = containerStore,
            onDismiss = {
                showingEditDialog = false
                itemToEdit = null
            }
        )
    }
    
    // Lösch-Dialog
    if (showingDeleteDialog && itemToDelete != null) {
        AlertDialog(
            onDismissRequest = {
                showingDeleteDialog = false
                itemToDelete = null
            },
            title = { Text("Filament löschen") },
            text = {
                Text("Möchten Sie das Filament '${itemToDelete!!.getDisplayTitle()}' wirklich löschen?")
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        scope.launch {
                            containerStore.removeItem(itemToDelete!!.id)
                            showingDeleteDialog = false
                            itemToDelete = null
                        }
                    }
                ) {
                    Text("Löschen", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(
                    onClick = {
                        showingDeleteDialog = false
                        itemToDelete = null
                    }
                ) {
                    Text("Abbrechen")
                }
            }
        )
    }
}

@Composable
fun ItemOverviewCard(
    item: Item,
    containers: List<Container>,
    onDelete: () -> Unit,
    onEdit: () -> Unit
) {
    // Finde Container-Name für dieses Item
    val containerName = remember(item.containerId, containers) {
        item.containerId?.let { containerId ->
            containers.firstOrNull { it.id == containerId }?.name
        } ?: "Lose"
    }
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Farbiger Kreis
            item.itemData?.color_hex?.let { colorHex ->
                val trimmedHex = colorHex.trim()
                if (trimmedHex.isNotEmpty()) {
                    val hexString = if (trimmedHex.startsWith("#")) {
                        trimmedHex
                    } else {
                        "#$trimmedHex"
                    }
                    val colorValue = remember(trimmedHex) {
                        try {
                            val hexWithoutHash = hexString.substring(1)
                            if (hexWithoutHash.length == 6 || hexWithoutHash.length == 8) {
                                val parsedColor = AndroidColor.parseColor(hexString)
                                Color(parsedColor)
                            } else {
                                null
                            }
                        } catch (e: Exception) {
                            null
                        }
                    }
                    if (colorValue != null) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .background(
                                    color = colorValue,
                                    shape = CircleShape
                                )
                                .border(
                                    width = 2.dp,
                                    color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                                    shape = CircleShape
                                )
                        )
                    } else {
                        // Platzhalter wenn Farbe ungültig
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .background(
                                    color = MaterialTheme.colorScheme.surfaceVariant,
                                    shape = CircleShape
                                )
                        ) {
                            Text(
                                text = "n/a",
                                style = MaterialTheme.typography.labelSmall,
                                modifier = Modifier.align(Alignment.Center),
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                } else {
                    // Platzhalter wenn Farbe leer
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .background(
                                color = MaterialTheme.colorScheme.surfaceVariant,
                                shape = CircleShape
                            )
                    ) {
                        Text(
                            text = "n/a",
                            style = MaterialTheme.typography.labelSmall,
                            modifier = Modifier.align(Alignment.Center),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } ?: run {
                // Platzhalter wenn keine Farbe vorhanden
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .background(
                            color = MaterialTheme.colorScheme.surfaceVariant,
                            shape = CircleShape
                        )
                ) {
                    Text(
                        text = "n/a",
                        style = MaterialTheme.typography.labelSmall,
                        modifier = Modifier.align(Alignment.Center),
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
            
            // Item-Informationen
            Column(
                modifier = Modifier.weight(1f)
            ) {
                // Zeile 1: Überschrift mit Farbe rechts
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = item.getDisplayTitle(),
                        style = MaterialTheme.typography.titleSmall,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f)
                    )
                    
                    // Farbe in runden Klammern
                    val colorText = remember(item.itemData) {
                        val color = item.itemData?.color?.trim()?.takeIf { it.isNotEmpty() }
                        if (color != null) {
                            "($color)"
                        } else {
                            item.itemData?.color_hex?.let { colorHex ->
                                val trimmedHex = colorHex.trim()
                                if (trimmedHex.isNotEmpty()) {
                                    val hexString = if (trimmedHex.startsWith("#")) {
                                        trimmedHex
                                    } else {
                                        "#$trimmedHex"
                                    }
                                    "($hexString)"
                                } else {
                                    null
                                }
                            } ?: ""
                        }
                    }
                    
                    if (colorText.isNotEmpty()) {
                        Text(
                            text = colorText,
                            style = MaterialTheme.typography.titleSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
                
                Spacer(modifier = Modifier.height(4.dp))
                
                // Zeile 2: Zwei Spalten - Links Daten, Rechts Icons
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.Top
                ) {
                    // Linke Spalte: Daten
                    Column(
                        modifier = Modifier.weight(1f)
                    ) {
                        // Type
                        item.itemData?.type?.let { type ->
                            Row(
                                horizontalArrangement = Arrangement.spacedBy(4.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = "Typ:",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Text(
                                    text = type,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                        }
                        
                        // Brand
                        item.itemData?.brand?.let { brand ->
                            Row(
                                horizontalArrangement = Arrangement.spacedBy(4.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = "Marke:",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Text(
                                    text = brand,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                        }
                        
                        // Container
                        Row(
                            horizontalArrangement = Arrangement.spacedBy(4.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = "Container:",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Text(
                                text = containerName,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                    
                    // Rechte Spalte: Icons
                    Row(
                        horizontalArrangement = Arrangement.End,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        IconButton(
                            onClick = onEdit,
                            modifier = Modifier.size(40.dp)
                        ) {
                            Icon(
                                Icons.Default.Edit,
                                contentDescription = "Bearbeiten",
                                tint = MaterialTheme.colorScheme.primary
                            )
                        }
                        
                        IconButton(
                            onClick = onDelete,
                            modifier = Modifier.size(40.dp)
                        ) {
                            Icon(
                                Icons.Default.Delete,
                                contentDescription = "Löschen",
                                tint = MaterialTheme.colorScheme.error
                            )
                        }
                    }
                }
            }
        }
    }
}
