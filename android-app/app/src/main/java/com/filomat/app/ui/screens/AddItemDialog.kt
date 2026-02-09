package com.filomat.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import android.graphics.Color as AndroidColor
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
import com.filomat.app.nfc.NFCReader

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddItemDialog(
    containerId: String,
    onDismiss: () -> Unit,
    onAdd: (Item) -> Unit,
    containerStore: ContainerStore
) {
    val context = LocalContext.current
    val nfcReader = remember { NFCReader(context as android.app.Activity) }
    val scope = rememberCoroutineScope()
    
    var isScanning by remember { mutableStateOf(false) }
    var scannedItem by remember { mutableStateOf<Item?>(null) }
    var scannedItemData by remember { mutableStateOf<ItemJsonData?>(null) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var isProcessing by remember { mutableStateOf(false) } // Verhindert doppelte Verarbeitung
    
    val isNfcAvailable by remember { mutableStateOf(nfcReader.isAvailable) }
    val nfcErrorMessage by nfcReader.errorMessage.collectAsState()
    
    // Starte automatisch den Scan beim Öffnen des Dialogs
    LaunchedEffect(Unit) {
        if (isNfcAvailable) {
            isScanning = true
            nfcReader.startScanningForItem { tag, itemData ->
                // Verhindere doppelte Verarbeitung
                if (isProcessing) return@startScanningForItem
                
                // Prüfe ob Tag gültig ist
                if (tag.isBlank()) {
                    errorMessage = "Ungültiger RFID-Tag."
                    isScanning = false
                    nfcReader.stopScanning()
                    return@startScanningForItem
                }
                
                isProcessing = true
                
                scannedItemData = itemData
                scope.launch {
                    // Prüfe ob Item bereits existiert
                    val existingItem = containerStore.findItemByTag(tag)
                    
                    if (existingItem != null) {
                        // Item existiert bereits
                        scannedItem = existingItem
                        
                        if (existingItem.containerId == containerId) {
                            // Item ist bereits in diesem Container
                            errorMessage = "Dieses Filament ist bereits in diesem Container."
                            isScanning = false
                            nfcReader.stopScanning()
                        } else if (existingItem.containerId != null) {
                            // Item ist in einem anderen Container - umziehen
                            // Verwende atomare Verschiebe-Funktion, um Datenverlust zu vermeiden
                            containerStore.moveItemToContainer(existingItem.id, existingItem.containerId, containerId)
                            statusMessage = "Filament wurde in diesen Container verschoben."
                            isScanning = false
                            nfcReader.stopScanning()
                            // Dialog nach kurzer Verzögerung schließen
                            delay(1500)
                            onDismiss()
                        } else {
                            // Item existiert, ist aber in keinem Container
                            containerStore.addItemToContainer(existingItem.id, containerId)
                            statusMessage = "Filament wurde hinzugefügt."
                            isScanning = false
                            nfcReader.stopScanning()
                            delay(1500)
                            onDismiss()
                        }
                    } else {
                        // Item existiert nicht - erstelle es
                        val name = if (itemData != null) {
                            "${itemData.brand ?: ""} ${itemData.type ?: ""}".trim().ifEmpty { "Filament" }
                        } else {
                            "Filament"
                        }
                        
                        val newItem = Item(
                            rfidTag = tag,
                            name = name,
                            description = "",
                            containerId = containerId,
                            itemData = itemData
                        )
                        
                        // Füge Item hinzu - addItemToContainer wird nicht benötigt, da containerId bereits gesetzt ist
                        // addItem fügt das Item automatisch zum Container hinzu, wenn containerId gesetzt ist
                        containerStore.addItem(newItem)
                        statusMessage = "Filament wurde erstellt und hinzugefügt."
                        isScanning = false
                        nfcReader.stopScanning()
                        delay(1500)
                        onDismiss()
                    }
                }
            }
        }
    }
    
    AlertDialog(
        onDismissRequest = {
            // Erlaube Abbrechen auch während des Scanvorgangs
            nfcReader.stopScanning()
            isScanning = false
            isProcessing = false
            nfcReader.stopScanning()
            onDismiss()
        },
        title = { Text("Filament scannen") },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(16.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                if (isScanning) {
                    CircularProgressIndicator(modifier = Modifier.size(48.dp))
                    Spacer(modifier = Modifier.height(16.dp))
                    Text(
                        text = "Bitte halten Sie ein Filament-Tag an Ihr Gerät.",
                        style = MaterialTheme.typography.bodyMedium,
                        textAlign = TextAlign.Center
                    )
                } else {
                    scannedItem?.let { item ->
                        // Zeige gescanntes Item an
                        ItemStatusCardSimple(item = item)
                    }
                }
                
                statusMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.primary,
                        style = MaterialTheme.typography.bodyMedium
                    )
                }
                
                errorMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodyMedium
                    )
                }
                
                nfcErrorMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
            }
        },
        confirmButton = {
            if (scannedItem != null) {
                null // Kein Button wenn Item gescannt wurde
            } else {
                TextButton(
                    onClick = {
                        nfcReader.stopScanning()
                        isScanning = false
                        isProcessing = false
                        onDismiss()
                    },
                    enabled = true
                ) {
                    Text("Abbrechen")
                }
            }
        },
        dismissButton = null
    )
}

@Composable
private fun ItemStatusCardSimple(item: Item) {
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
