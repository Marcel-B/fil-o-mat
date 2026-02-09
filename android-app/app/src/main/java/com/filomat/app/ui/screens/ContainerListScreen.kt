package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Archive
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
import com.filomat.app.ui.viewmodel.ContainerViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContainerListScreen(
    navController: androidx.navigation.NavController,
    viewModel: ContainerViewModel = viewModel()
) {
    val context = LocalContext.current
    val containerStore = remember { ContainerStore(context) }
    val containers by viewModel.containers.collectAsState(initial = emptyList())
    var showingAddContainer by remember { mutableStateOf(false) }
    
    LaunchedEffect(Unit) {
        containerStore.containers.collect { containerList ->
            viewModel.updateContainers(containerList)
        }
    }
    
    Scaffold(
        topBar = {
            TopAppBar(title = { Text("Container") })
        },
        bottomBar = {
            NavigationBar {
                NavigationBarItem(
                    selected = true,
                    onClick = { },
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
                    selected = false,
                    onClick = { navController.navigate("items") },
                    icon = { Icon(Icons.Default.Search, contentDescription = null) },
                    label = { Text("Filamente") }
                )
            }
        },
        floatingActionButton = {
            FloatingActionButton(onClick = { showingAddContainer = true }) {
                Icon(Icons.Default.Add, contentDescription = "Hinzufügen")
            }
        }
    ) { padding ->
        if (containers.isEmpty()) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = Alignment.Center
            ) {
                Text("Keine Container vorhanden")
            }
        } else {
            val regularContainers = containers.filter { !it.isStandard }
            val standardContainers = containers.filter { it.isStandard }
            
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
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
