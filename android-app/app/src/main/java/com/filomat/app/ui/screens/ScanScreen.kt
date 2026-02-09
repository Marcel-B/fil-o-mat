package com.filomat.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import android.graphics.Color as AndroidColor
import androidx.lifecycle.viewmodel.compose.viewModel
import com.filomat.app.data.ContainerStore
import com.filomat.app.nfc.NFCReader
import com.filomat.app.ui.viewmodel.ScanViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ScanScreen(
    navController: androidx.navigation.NavController,
    viewModel: ScanViewModel = viewModel()
) {
    val context = LocalContext.current
    val containerStore = remember { ContainerStore(context) }
    val nfcReader = remember { NFCReader(context as android.app.Activity) }
    
    val scanMode by viewModel.scanMode.collectAsState()
    val scannedContainer by viewModel.scannedContainer.collectAsState()
    val scannedItem by viewModel.scannedItem.collectAsState()
    val currentStep by viewModel.currentStep.collectAsState()
    val isScanning by nfcReader.isScanning.collectAsState()
    val errorMessage by nfcReader.errorMessage.collectAsState()
    val viewModelError by viewModel.errorMessage.collectAsState()
    val warningMessage by viewModel.warningMessage.collectAsState()
    val showConfirmDialog by viewModel.showConfirmDialog.collectAsState()
    
    val isNfcAvailable by remember { mutableStateOf(nfcReader.isAvailable) }
    
    LaunchedEffect(Unit) {
        nfcReader.lastScannedTag.collect { tag ->
            tag?.let { 
                val itemData = nfcReader.lastScannedItemData.value
                viewModel.handleScannedTag(it, itemData, containerStore)
                // Stoppe den Scan nach erfolgreichem Lesen
                nfcReader.stopScanning()
            }
        }
    }
    
    Scaffold(
        topBar = {
            TopAppBar(title = { Text("Scannen") })
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
                    selected = true,
                    onClick = { },
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
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Modus-Auswahl
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                FilterChip(
                    selected = scanMode == ScanMode.ADD_TO_CONTAINER,
                    onClick = { viewModel.setScanMode(ScanMode.ADD_TO_CONTAINER) },
                    label = { Text("Einlagern") },
                    modifier = Modifier.weight(1f)
                )
                FilterChip(
                    selected = scanMode == ScanMode.REMOVE_FROM_CONTAINER,
                    onClick = { viewModel.setScanMode(ScanMode.REMOVE_FROM_CONTAINER) },
                    label = { Text("Herausnehmen") },
                    modifier = Modifier.weight(1f)
                )
            }
            
            // Anleitung
            Card(
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Text(
                        text = if (scanMode == ScanMode.ADD_TO_CONTAINER) {
                            if (currentStep == 0) {
                                "Schritt 1: Container scannen"
                            } else {
                                "Schritt 2: Filament scannen"
                            }
                        } else {
                            if (currentStep == 0) {
                                "Schritt 1: Filament scannen"
                            } else {
                                "Schritt 2: Container scannen"
                            }
                        },
                        style = MaterialTheme.typography.titleMedium
                    )
                    Text(
                        text = if (scanMode == ScanMode.ADD_TO_CONTAINER) {
                            if (currentStep == 0) {
                                "Scannen Sie zuerst den Container, in den Sie das Filament einlagern möchten."
                            } else {
                                "Scannen Sie nun das Filament, das Sie in den Container einlagern möchten."
                            }
                        } else {
                            if (currentStep == 0) {
                                "Scannen Sie zuerst das Filament, das Sie aus dem Container herausnehmen möchten."
                            } else {
                                "Scannen Sie nun den Container, aus dem Sie das Filament herausnehmen möchten."
                            }
                        },
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
            
            // Scan-Button
            if (isNfcAvailable) {
                Button(
                    onClick = {
                        if (scanMode == ScanMode.ADD_TO_CONTAINER && currentStep == 1) {
                            // Item scannen - verwende startScanningForItem
                            nfcReader.startScanningForItem { tag, itemData ->
                                viewModel.handleScannedTag(tag, itemData, containerStore)
                                nfcReader.stopScanning()
                            }
                        } else {
                            // Container scannen - normale Scan-Funktion
                            nfcReader.startScanning { }
                        }
                    },
                    enabled = !isScanning,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    if (isScanning) {
                        CircularProgressIndicator(modifier = Modifier.size(16.dp))
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Scanne...")
                    } else {
                        Text("Tag scannen")
                    }
                }
            } else {
                Text(
                    text = "NFC ist auf diesem Gerät nicht verfügbar.",
                    color = MaterialTheme.colorScheme.error
                )
            }
            
            // Status-Anzeige
            scannedContainer?.let { container ->
                ContainerStatusCard(container = container)
            }
            
            scannedItem?.let { item ->
                ItemStatusCard(item = item)
            }
            
            warningMessage?.let {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )
                ) {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(16.dp)
                    )
                }
            }
            
            viewModelError?.let {
                Text(
                    text = it,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium
                )
            }
            
            errorMessage?.let {
                Text(
                    text = it,
                    color = MaterialTheme.colorScheme.error
                )
            }
            
            Spacer(modifier = Modifier.weight(1f))
            
            // Aktion-Button
            if (scannedContainer != null && scannedItem != null && warningMessage == null) {
                Button(
                    onClick = {
                        viewModel.performAction(containerStore) {
                            nfcReader.stopScanning()
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(
                        if (scanMode == ScanMode.ADD_TO_CONTAINER) {
                            "Filament einlagern"
                        } else {
                            "Filament herausnehmen"
                        }
                    )
                }
            }
            
            // Bestätigungsdialog für Item-Umzug
            if (showConfirmDialog) {
                AlertDialog(
                    onDismissRequest = { viewModel.cancelMoveItem() },
                    title = { Text("Filament umlagern?") },
                    text = {
                        Text(
                            "Dieses Filament ist bereits in einem anderen Container. Möchten Sie es in den neuen Container umlagern?"
                        )
                    },
                    confirmButton = {
                        TextButton(
                            onClick = {
                                viewModel.confirmMoveItem()
                            }
                        ) {
                            Text("Ja, umlagern")
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { viewModel.cancelMoveItem() }) {
                            Text("Abbrechen")
                        }
                    }
                )
            }
        }
    }
}

@Composable
fun StatusCard(title: String, value: String) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Icon(
                imageVector = Icons.Default.CheckCircle,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
            Column {
                Text(
                    text = title,
                    style = MaterialTheme.typography.titleSmall
                )
                Text(
                    text = value,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

@Composable
fun ContainerStatusCard(container: com.filomat.app.data.Container) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Icon(
                imageVector = Icons.Default.CheckCircle,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
            Column(
                modifier = Modifier.weight(1f)
            ) {
                Text(
                    text = "Container erkannt",
                    style = MaterialTheme.typography.titleSmall
                )
                Text(
                    text = container.name,
                    style = MaterialTheme.typography.bodyMedium
                )
                if (container.description.isNotEmpty()) {
                    Text(
                        text = container.description,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
    }
}

@Composable
fun ItemStatusCard(item: com.filomat.app.data.Item) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Icon(
                imageVector = Icons.Default.CheckCircle,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
            
            // Farbiger Kreis für color_hex
            item.itemData?.color_hex?.let { colorHex ->
                val trimmedHex = colorHex.trim()
                if (trimmedHex.isNotEmpty()) {
                    val colorValue = remember(trimmedHex) {
                        try {
                            val hexString = if (trimmedHex.startsWith("#")) {
                                trimmedHex
                            } else {
                                "#$trimmedHex"
                            }
                            // Prüfe ob es ein gültiger Hex-String ist (6 oder 8 Zeichen nach #)
                            val hexWithoutHash = hexString.substring(1)
                            if (hexWithoutHash.length == 6 || hexWithoutHash.length == 8) {
                                val parsedColor = AndroidColor.parseColor(hexString)
                                parsedColor
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
                                    color = Color(colorValue),
                                    shape = CircleShape
                                )
                                .border(
                                    width = 2.dp,
                                    color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                                    shape = CircleShape
                                )
                        )
                    }
                }
            }
            
            Column(
                modifier = Modifier.weight(1f)
            ) {
                Text(
                    text = "Filament erkannt",
                    style = MaterialTheme.typography.titleSmall
                )
                Text(
                    text = item.getDisplayTitle(),
                    style = MaterialTheme.typography.bodyMedium
                )
                // Farbe immer anzeigen (mit Kreis oder n/a)
                Row(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Farbe:",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
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
                                        parsedColor
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
                                        .size(16.dp)
                                        .background(
                                            color = Color(colorValue),
                                            shape = CircleShape
                                        )
                                        .border(
                                            width = 1.dp,
                                            color = MaterialTheme.colorScheme.outline.copy(alpha = 0.5f),
                                            shape = CircleShape
                                        )
                                )
                            }
                            Text(
                                text = item.itemData?.color?.trim()?.takeIf { it.isNotEmpty() } ?: hexString,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        } else {
                            Text(
                                text = "n/a",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    } ?: run {
                        Text(
                            text = "n/a",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }
    }
}

enum class ScanMode {
    ADD_TO_CONTAINER,
    REMOVE_FROM_CONTAINER
}
