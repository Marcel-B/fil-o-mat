package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Cloud
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material.icons.filled.Warning
import androidx.compose.ui.graphics.vector.ImageVector
import com.filomat.app.data.sync.BackendUpdateStatus
import kotlinx.coroutines.launch
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.ui.components.MainNavigationBar
import com.filomat.app.ui.components.MainTab
import com.filomat.app.ui.viewmodel.ContainerViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContainerListScreen(
    navController: androidx.navigation.NavController,
    viewModel: ContainerViewModel = viewModel()
) {
    val context = LocalContext.current
    val containerStore = remember { ContainerStore.get(context) }
    val containers by viewModel.containers.collectAsState(initial = emptyList())
    var showingAddContainer by remember { mutableStateOf(false) }
    val sync by containerStore.sync.collectAsState()
    val scope = rememberCoroutineScope()
    
    LaunchedEffect(Unit) {
        containerStore.containers.collect { containerList ->
            viewModel.updateContainers(containerList)
        }
    }
    
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Container") },
                actions = {
                    if (sync.connected) {
                        if (sync.isSyncing) {
                            CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
                        } else {
                            IconButton(onClick = {
                                scope.launch {
                                    containerStore.refresh()
                                    containerStore.refreshUpdateStatus()
                                }
                            }) {
                                Icon(Icons.Default.Refresh, contentDescription = "Abgleichen")
                            }
                        }
                    }
                    IconButton(onClick = { navController.navigate("backend") }) {
                        Icon(
                            if (sync.connected) Icons.Default.Cloud else Icons.Default.CloudOff,
                            contentDescription = "Backend"
                        )
                    }
                }
            )
        },
        bottomBar = { MainNavigationBar(navController, MainTab.CONTAINERS) },
        floatingActionButton = {
            FloatingActionButton(onClick = { showingAddContainer = true }) {
                Icon(Icons.Default.Add, contentDescription = "Hinzufügen")
            }
        }
    ) { padding ->
        Column(modifier = Modifier.padding(padding)) {
            SyncBanners(
                error = sync.error,
                update = sync.updateStatus,
                onClick = { navController.navigate("backend") }
            )
            ContainerList(containers, navController)
        }

        if (showingAddContainer) {
            AddContainerDialog(
                onDismiss = { showingAddContainer = false },
                onAdd = { container ->
                    viewModel.addContainer(container, containerStore)
                    showingAddContainer = false
                }
            )
        }
    }
}

/** Hinweise auf Abgleich-Fehler und neue Backend-Versionen, wie in der iOS-App. */
@Composable
private fun SyncBanners(error: String?, update: BackendUpdateStatus?, onClick: () -> Unit) {
    if (error != null) {
        Banner(Icons.Default.CloudOff, error, isError = true, onClick = onClick)
    }
    if (update != null && (update.updateAvailable || update.failed)) {
        when {
            update.failed -> Banner(Icons.Default.Warning, "Backend-Update fehlgeschlagen", isError = true, onClick = onClick)
            update.isUpdating -> Banner(Icons.Default.Sync, "Backend wird aktualisiert …", onClick = onClick)
            else -> Banner(Icons.Default.Download, "Neue Backend-Version verfügbar", onClick = onClick)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun Banner(icon: ImageVector, text: String, isError: Boolean = false, onClick: () -> Unit) {
    Card(
        onClick = onClick,
        colors = CardDefaults.cardColors(
            containerColor = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer
        ),
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 4.dp)
    ) {
        Row(modifier = Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(icon, contentDescription = null)
            Text(text, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
private fun ContainerList(containers: List<Container>, navController: androidx.navigation.NavController) {
    if (containers.isEmpty()) {
        Box(
            modifier = Modifier
                .fillMaxSize(),
            contentAlignment = Alignment.Center
        ) {
            Text("Keine Container vorhanden")
        }
    } else {
        val regularContainers = containers.filter { !it.isStandard }
        val standardContainers = containers.filter { it.isStandard }
        
        LazyColumn(
            modifier = Modifier
                .fillMaxSize(),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Normale Container
            items(regularContainers) { container ->
                ContainerCard(
                    container = container,
                    onClick = {
                        navController.navigate("container/${container.id}")
                    }
                )
            }
            
            // Trennlinie vor Standard-Containern
            if (regularContainers.isNotEmpty() && standardContainers.isNotEmpty()) {
                item {
                    Spacer(modifier = Modifier.height(16.dp))
                    Divider(
                        modifier = Modifier.padding(vertical = 8.dp),
                        thickness = 2.dp
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    Text(
                        text = "Standard-Container",
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(vertical = 8.dp)
                    )
                }
            }
            
            // Standard-Container
            items(standardContainers) { container ->
                ContainerCard(
                    container = container,
                    onClick = {
                        navController.navigate("container/${container.id}")
                    }
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContainerCard(
    container: Container,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            Text(
                text = container.name,
                style = MaterialTheme.typography.headlineSmall
            )
            Text(
                text = "RFID: ${container.rfidTag}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text(
                text = "${container.items.size} Filamente",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}
