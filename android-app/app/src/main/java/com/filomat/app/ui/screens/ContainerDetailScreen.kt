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
    val containerStore = remember { ContainerStore.get(context) }
    val container by viewModel.container.collectAsState()
    val storeState by containerStore.state.collectAsState(initial = com.filomat.app.data.sync.StoreState())
    var showingEditContainer by remember { mutableStateOf(false) }
    var showingDeleteDialog by remember { mutableStateOf(false) }
    
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
            ExtendedFloatingActionButton(
                onClick = { navController.navigate("scan/place/container") },
                icon = { Icon(Icons.Default.Add, contentDescription = null) },
                text = { Text("Einlagern") }
            )
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
                
                // Der Container eines Druckers: was in seinen Köpfen oder Slots steckt
                val printer = storeState.printerOwning(cont.id)
                if (printer != null) {
                    item { PrinterContent(printer, storeState) }
                }
                // Sorten stehen unter "Filamente"; ein Container kennt nur die Spulen, die darin liegen
                val spools = storeState.spoolsIn(cont.id)
                if (spools.isNotEmpty()) {
                    item { OpenedSpoolsCard(spools, storeState) }
                } else if (printer == null) {
                    item {
                        Text(
                            text = "Keine Spulen in diesem Container",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
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
        
        if (showingDeleteDialog && container != null) {
            AlertDialog(
                onDismissRequest = { showingDeleteDialog = false },
                title = { Text("Container löschen") },
                text = { 
                    Text("Möchten Sie den Container '${container!!.name}' wirklich löschen? Die Spulen darin stehen danach unter 'Lose'.")
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
            style = MaterialTheme.typography.bodyMedium,
            textAlign = androidx.compose.ui.text.style.TextAlign.End,
            modifier = Modifier
                .padding(start = 16.dp)
                .weight(1f, fill = false)
        )
    }
}
