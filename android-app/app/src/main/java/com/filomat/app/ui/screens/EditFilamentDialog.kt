package com.filomat.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import android.graphics.Color as AndroidColor
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import com.filomat.app.nfc.NFCReader
import com.filomat.app.data.FilamentPresetDatabase
import com.filomat.app.data.Item
import com.filomat.app.data.ContainerStore
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import android.nfc.Tag
import androidx.compose.ui.Alignment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditFilamentDialog(
    item: Item,
    containerStore: ContainerStore,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    val nfcReader = remember { NFCReader(context as android.app.Activity) }
    val presetDatabase = remember { FilamentPresetDatabase(context) }
    val scope = rememberCoroutineScope()
    
    val filamentTypes = listOf("PLA", "TPU", "PETG", "ASA", "ABS")
    
    // Initialisiere mit bestehenden Werten
    var selectedType by remember { mutableStateOf(item.itemData?.type ?: "") }
    var isTypeExpanded by remember { mutableStateOf(false) }
    var brand by remember { mutableStateOf(item.itemData?.brand ?: "") }
    var isBrandExpanded by remember { mutableStateOf(false) }
    var variant by remember { mutableStateOf(item.itemData?.variant ?: "") }
    var isVariantExpanded by remember { mutableStateOf(false) }
    
    // Lade Marken aus JSON-Datei
    var availableBrands by remember { mutableStateOf<List<String>>(emptyList()) }
    var availableVariants by remember { mutableStateOf<List<String>>(emptyList()) }
    
    LaunchedEffect(Unit) {
        // Lade Brands und Variants aus der Datenbank
        availableBrands = withContext(Dispatchers.IO) {
            try {
                presetDatabase.getAllBrands()
            } catch (e: Exception) {
                android.util.Log.e("EditFilament", "Fehler beim Laden der Marken: ${e.message}")
                emptyList()
            }
        }
        
        availableVariants = withContext(Dispatchers.IO) {
            try {
                presetDatabase.getAllVariants()
            } catch (e: Exception) {
                android.util.Log.e("EditFilament", "Fehler beim Laden der Varianten: ${e.message}")
                emptyList()
            }
        }
    }
    
    // Gefilterte Marken basierend auf Eingabe
    val filteredBrands = remember(brand, availableBrands) {
        if (brand.isEmpty()) {
            availableBrands
        } else {
            availableBrands.filter { it.contains(brand, ignoreCase = true) }
        }
    }
    
    // Gefilterte Varianten basierend auf Eingabe
    val filteredVariants = remember(variant, availableVariants) {
        if (variant.isEmpty()) {
            availableVariants
        } else {
            availableVariants.filter { it.contains(variant, ignoreCase = true) }
        }
    }
    
    // Initialisiere mit bestehenden Werten
    var colorHex by remember { mutableStateOf(item.itemData?.color_hex ?: "") }
    var color by remember { mutableStateOf(item.itemData?.color ?: "") }
    var minTemp by remember { mutableStateOf(item.itemData?.min_temp ?: "") }
    var maxTemp by remember { mutableStateOf(item.itemData?.max_temp ?: "") }
    var bedMinTemp by remember { mutableStateOf(item.itemData?.bed_min_temp ?: "") }
    var bedMaxTemp by remember { mutableStateOf(item.itemData?.bed_max_temp ?: "") }
    var isProcessing by remember { mutableStateOf(false) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusError by remember { mutableStateOf<String?>(null) }
    var writeToTag by remember { mutableStateOf(false) }
    
    val isNfcAvailable by remember { mutableStateOf(nfcReader.isAvailable) }
    val errorMessage by nfcReader.errorMessage.collectAsState()
    
    // Json-Instanz mit encodeDefaults = true, damit alle Werte geschrieben werden
    val json = Json {
        encodeDefaults = true
    }
    
    @Serializable
    data class FilamentJsonData(
        val protocol: String = "openspool",
        val version: String = "1.0",
        val type: String,
        val brand: String? = null,
        val variant: String? = null,
        val color_hex: String? = null,
        val color: String? = null,
        val min_temp: String? = null,
        val max_temp: String? = null,
        val bed_min_temp: String? = null,
        val bed_max_temp: String? = null
    )
    
    // Zustand für den Workflow: 0 = Grunddaten, 1 = Temperaturen, 2 = Speichern (optional: Tag beschreiben)
    var workflowState by remember { mutableStateOf(0) }
    
    // Funktion zum Laden der Temperaturen aus der Datenbank
    fun loadTemperatures(type: String?, brand: String?) {
        if (type != null) {
            scope.launch(Dispatchers.IO) {
                val preset = presetDatabase.getPreset(type, brand?.takeIf { it.isNotEmpty() })
                withContext(Dispatchers.Main) {
                    if (preset != null) {
                        // Nur setzen wenn noch nicht vorhanden
                        if (minTemp.isEmpty()) minTemp = preset.minTemp
                        if (maxTemp.isEmpty()) maxTemp = preset.maxTemp
                        if (bedMinTemp.isEmpty()) bedMinTemp = preset.bedMinTemp ?: ""
                        if (bedMaxTemp.isEmpty()) bedMaxTemp = preset.bedMaxTemp ?: ""
                    }
                }
            }
        }
    }
    
    // Lade Temperaturen automatisch, wenn Typ oder Marke geändert werden
    LaunchedEffect(selectedType, brand) {
        if (selectedType.isNotEmpty() && workflowState == 0) {
            loadTemperatures(selectedType, brand)
        }
    }
    
    AlertDialog(
        onDismissRequest = {
            // Erlaube Abbrechen auch während des Schreibvorgangs
            nfcReader.stopScanning()
            isProcessing = false
            onDismiss()
        },
        title = { 
            Text(
                when (workflowState) {
                    1 -> "Temperaturen"
                    2 -> if (writeToTag) "Tag beschreiben" else "Speichern"
                    else -> "Filament bearbeiten"
                }
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                when (workflowState) {
                    0 -> {
                        // Eingabe-Phase
                        // Type-Auswahl
                        ExposedDropdownMenuBox(
                            expanded = isTypeExpanded,
                            onExpandedChange = { isTypeExpanded = it },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            OutlinedTextField(
                                value = selectedType,
                                onValueChange = { },
                                readOnly = true,
                                label = { Text("Typ *") },
                                trailingIcon = {
                                    IconButton(onClick = { isTypeExpanded = !isTypeExpanded }) {
                                        Icon(Icons.Default.ArrowDropDown, contentDescription = null)
                                    }
                                },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .menuAnchor(),
                                enabled = !isProcessing
                            )
                            ExposedDropdownMenu(
                                expanded = isTypeExpanded,
                                onDismissRequest = { isTypeExpanded = false }
                            ) {
                                filamentTypes.forEach { type ->
                                    DropdownMenuItem(
                                        text = { Text(type) },
                                        onClick = {
                                            selectedType = type
                                            isTypeExpanded = false
                                            // Lade Temperaturen für Typ und Marke
                                            scope.launch {
                                                loadTemperatures(type, brand)
                                            }
                                        }
                                    )
                                }
                            }
                        }
                        
                        // Brand mit Autocomplete
                        ExposedDropdownMenuBox(
                            expanded = isBrandExpanded,
                            onExpandedChange = { isBrandExpanded = it },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            OutlinedTextField(
                                value = brand,
                                onValueChange = { 
                                    brand = it
                                    isBrandExpanded = it.isNotEmpty() && filteredBrands.isNotEmpty()
                                },
                                label = { Text("Marke") },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .menuAnchor(),
                                singleLine = true,
                                enabled = !isProcessing,
                                trailingIcon = {
                                    if (filteredBrands.isNotEmpty()) {
                                        IconButton(onClick = { isBrandExpanded = !isBrandExpanded }) {
                                            Icon(Icons.Default.ArrowDropDown, contentDescription = null)
                                        }
                                    }
                                }
                            )
                            if (filteredBrands.isNotEmpty()) {
                                ExposedDropdownMenu(
                                    expanded = isBrandExpanded,
                                    onDismissRequest = { isBrandExpanded = false }
                                ) {
                                    filteredBrands.forEach { brandOption ->
                                        DropdownMenuItem(
                                            text = { Text(brandOption) },
                                            onClick = {
                                                brand = brandOption
                                                isBrandExpanded = false
                                                // Lade Temperaturen für Typ und Marke
                                                scope.launch {
                                                    loadTemperatures(selectedType, brandOption)
                                                }
                                            }
                                        )
                                    }
                                }
                            }
                        }
                        
                        // Variant mit Autocomplete
                        ExposedDropdownMenuBox(
                            expanded = isVariantExpanded,
                            onExpandedChange = { isVariantExpanded = it },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            OutlinedTextField(
                                value = variant,
                                onValueChange = { 
                                    variant = it
                                    isVariantExpanded = it.isNotEmpty() && filteredVariants.isNotEmpty()
                                },
                                label = { Text("Variante") },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .menuAnchor(),
                                singleLine = true,
                                enabled = !isProcessing,
                                trailingIcon = {
                                    if (filteredVariants.isNotEmpty()) {
                                        IconButton(onClick = { isVariantExpanded = !isVariantExpanded }) {
                                            Icon(Icons.Default.ArrowDropDown, contentDescription = null)
                                        }
                                    }
                                }
                            )
                            if (filteredVariants.isNotEmpty()) {
                                ExposedDropdownMenu(
                                    expanded = isVariantExpanded,
                                    onDismissRequest = { isVariantExpanded = false }
                                ) {
                                    filteredVariants.forEach { variantOption ->
                                        DropdownMenuItem(
                                            text = { Text(variantOption) },
                                            onClick = {
                                                variant = variantOption
                                                isVariantExpanded = false
                                            }
                                        )
                                    }
                                }
                            }
                        }
                        
                        // Color Hex mit Farbkreis
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            OutlinedTextField(
                                value = colorHex,
                                onValueChange = { colorHex = it },
                                label = { Text("Farbe (Hex)") },
                                modifier = Modifier.weight(1f),
                                singleLine = true,
                                enabled = !isProcessing,
                                placeholder = { Text("#FF0000") }
                            )
                            
                            // Farbiger Kreis
                            val colorValue = remember(colorHex) {
                                val trimmedHex = colorHex.trim()
                                if (trimmedHex.isNotEmpty()) {
                                    val hexString = if (trimmedHex.startsWith("#")) {
                                        trimmedHex
                                    } else {
                                        "#$trimmedHex"
                                    }
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
                                } else {
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
                        }
                        
                        // Color
                        OutlinedTextField(
                            value = color,
                            onValueChange = { color = it },
                            label = { Text("Farbe (Text)") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing,
                            placeholder = { Text("Rot") }
                        )
                        
                        if (statusError != null) {
                            Text(
                                text = statusError!!,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                        
                        if (errorMessage != null) {
                            Text(
                                text = errorMessage!!,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                    1 -> {
                        // Phase 1: Temperaturen
                        // Min Temp
                        OutlinedTextField(
                            value = minTemp,
                            onValueChange = { if (it.isEmpty() || it.all { char -> char.isDigit() }) minTemp = it },
                            label = { Text("Min. Temperatur (°C)") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                        )
                        
                        // Max Temp
                        OutlinedTextField(
                            value = maxTemp,
                            onValueChange = { if (it.isEmpty() || it.all { char -> char.isDigit() }) maxTemp = it },
                            label = { Text("Max. Temperatur (°C)") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                        )
                        
                        // Bed Min Temp
                        OutlinedTextField(
                            value = bedMinTemp,
                            onValueChange = { if (it.isEmpty() || it.all { char -> char.isDigit() }) bedMinTemp = it },
                            label = { Text("Min. Bett-Temperatur (°C)") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                        )
                        
                        // Bed Max Temp
                        OutlinedTextField(
                            value = bedMaxTemp,
                            onValueChange = { if (it.isEmpty() || it.all { char -> char.isDigit() }) bedMaxTemp = it },
                            label = { Text("Max. Bett-Temperatur (°C)") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                        )
                        
                        if (statusError != null) {
                            Text(
                                text = statusError!!,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                        
                        if (errorMessage != null) {
                            Text(
                                text = errorMessage!!,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                    2 -> {
                        // Phase 2: Speichern (optional: Tag beschreiben)
                        if (writeToTag) {
                            if (isProcessing) {
                                CircularProgressIndicator(modifier = Modifier.align(Alignment.CenterHorizontally))
                                Spacer(modifier = Modifier.height(16.dp))
                            }
                            Text(
                                text = "Halten Sie den NFC-Tag an das Gerät",
                                style = MaterialTheme.typography.bodyMedium
                            )
                            
                            if (statusMessage != null) {
                                Text(
                                    text = statusMessage!!,
                                    color = MaterialTheme.colorScheme.primary,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                            
                            if (statusError != null) {
                                Text(
                                    text = statusError!!,
                                    color = MaterialTheme.colorScheme.error,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                            
                            if (errorMessage != null) {
                                Text(
                                    text = errorMessage!!,
                                    color = MaterialTheme.colorScheme.error,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                        } else {
                            Text(
                                text = "Filament wird gespeichert...",
                                style = MaterialTheme.typography.bodyMedium
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            when (workflowState) {
                0 -> {
                    Button(
                        onClick = {
                            if (selectedType.isNotEmpty()) {
                                workflowState = 1
                                statusError = null
                                statusMessage = null
                            } else {
                                statusError = "Bitte wählen Sie einen Typ aus"
                            }
                        },
                        enabled = !isProcessing && selectedType.isNotEmpty()
                    ) {
                        Text("Weiter")
                    }
                }
                1 -> {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        TextButton(
                            onClick = {
                                workflowState = 0
                            },
                            enabled = true
                        ) {
                            Text("Zurück")
                        }
                        Button(
                            onClick = {
                                workflowState = 2
                                writeToTag = false
                                isProcessing = true
                                statusError = null
                                statusMessage = null
                                
                                // Speichere Filament-Daten
                                scope.launch(Dispatchers.IO) {
                                    try {
                                        // Erstelle aktualisiertes ItemJsonData
                                        val updatedItemData = com.filomat.app.data.ItemJsonData(
                                            protocol = item.itemData?.protocol ?: "openspool",
                                            version = item.itemData?.version ?: "1.0",
                                            type = selectedType.takeIf { it.isNotEmpty() },
                                            brand = brand.trim().takeIf { it.isNotEmpty() },
                                            variant = variant.trim().takeIf { it.isNotEmpty() },
                                            color_hex = colorHex.trim().takeIf { it.isNotEmpty() }?.let { hex ->
                                                if (hex.startsWith("#")) {
                                                    hex.substring(1)
                                                } else {
                                                    hex
                                                }
                                            },
                                            color = color.trim().takeIf { it.isNotEmpty() },
                                            min_temp = minTemp.trim().takeIf { it.isNotEmpty() },
                                            max_temp = maxTemp.trim().takeIf { it.isNotEmpty() },
                                            bed_min_temp = bedMinTemp.trim().takeIf { it.isNotEmpty() },
                                            bed_max_temp = bedMaxTemp.trim().takeIf { it.isNotEmpty() }
                                        )
                                        
                                        // Aktualisiere Item
                                        val updatedItem = item.copy(itemData = updatedItemData)
                                        containerStore.updateItem(updatedItem)
                                        
                                        withContext(Dispatchers.Main) {
                                            statusMessage = "Filament erfolgreich gespeichert!"
                                            delay(1000)
                                            onDismiss()
                                        }
                                    } catch (e: Exception) {
                                        withContext(Dispatchers.Main) {
                                            statusError = "Fehler beim Speichern: ${e.message}"
                                            isProcessing = false
                                            workflowState = 2
                                        }
                                    }
                                }
                            },
                            enabled = !isProcessing
                        ) {
                            Text("Speichern")
                        }
                        Button(
                            onClick = {
                                workflowState = 2
                                writeToTag = true
                                isProcessing = true
                                statusError = null
                                statusMessage = null
                                
                                // Hex ohne # speichern - außerhalb des try-Blocks definieren
                                val processedColorHex = colorHex.trim().takeIf { it.isNotEmpty() }?.let { hex ->
                                    if (hex.startsWith("#")) {
                                        hex.substring(1)
                                    } else {
                                        hex
                                    }
                                }
                                
                                // Erstelle JSON-Daten
                                val jsonData = try {
                                    val filamentData = FilamentJsonData(
                                        protocol = item.itemData?.protocol ?: "openspool",
                                        version = item.itemData?.version ?: "1.0",
                                        type = selectedType,
                                        brand = brand.trim().takeIf { it.isNotEmpty() },
                                        variant = variant.trim().takeIf { it.isNotEmpty() },
                                        color_hex = processedColorHex,
                                        color = color.trim().takeIf { it.isNotEmpty() },
                                        min_temp = minTemp.trim().takeIf { it.isNotEmpty() },
                                        max_temp = maxTemp.trim().takeIf { it.isNotEmpty() },
                                        bed_min_temp = bedMinTemp.trim().takeIf { it.isNotEmpty() },
                                        bed_max_temp = bedMaxTemp.trim().takeIf { it.isNotEmpty() }
                                    )
                                    val encoded = json.encodeToString(filamentData)
                                    // Debug: Zeige das generierte JSON
                                    android.util.Log.d("EditFilament", "JSON: $encoded")
                                    encoded
                                } catch (e: Exception) {
                                    statusError = "Fehler beim Erstellen der Daten: ${e.message}"
                                    isProcessing = false
                                    workflowState = 1
                                    return@Button
                                }
                                
                                // Starte NFC-Schreibvorgang
                                nfcReader.startWriting { tag ->
                                    scope.launch {
                                        try {
                                            val success = nfcReader.writeJsonTag(jsonData, tag)
                                            if (success) {
                                                // Speichere auch in der App
                                                val updatedItemData = com.filomat.app.data.ItemJsonData(
                                                    protocol = item.itemData?.protocol ?: "openspool",
                                                    version = item.itemData?.version ?: "1.0",
                                                    type = selectedType.takeIf { it.isNotEmpty() },
                                                    brand = brand.trim().takeIf { it.isNotEmpty() },
                                                    variant = variant.trim().takeIf { it.isNotEmpty() },
                                                    color_hex = processedColorHex,
                                                    color = color.trim().takeIf { it.isNotEmpty() },
                                                    min_temp = minTemp.trim().takeIf { it.isNotEmpty() },
                                                    max_temp = maxTemp.trim().takeIf { it.isNotEmpty() },
                                                    bed_min_temp = bedMinTemp.trim().takeIf { it.isNotEmpty() },
                                                    bed_max_temp = bedMaxTemp.trim().takeIf { it.isNotEmpty() }
                                                )
                                                
                                                val updatedItem = item.copy(itemData = updatedItemData)
                                                containerStore.updateItem(updatedItem)
                                                
                                                statusMessage = "Tag erfolgreich beschrieben und gespeichert!"
                                                delay(1000)
                                                onDismiss()
                                            } else {
                                                statusError = "Fehler beim Beschreiben des Tags"
                                                isProcessing = false
                                                workflowState = 2
                                            }
                                        } catch (e: Exception) {
                                            statusError = "Fehler: ${e.message ?: e.javaClass.simpleName}"
                                            isProcessing = false
                                            workflowState = 2
                                        }
                                    }
                                }
                            },
                            enabled = !isProcessing && isNfcAvailable
                        ) {
                            Text("Speichern & Tag")
                        }
                    }
                }
                2 -> {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        if (writeToTag) {
                            TextButton(
                                onClick = {
                                    nfcReader.stopScanning()
                                    isProcessing = false
                                    workflowState = 1
                                    statusError = null
                                    statusMessage = null
                                },
                                enabled = true
                            ) {
                                Text("Zurück")
                            }
                        }
                        TextButton(
                            onClick = {
                                nfcReader.stopScanning()
                                isProcessing = false
                                onDismiss()
                            },
                            enabled = true
                        ) {
                            Text("Abbrechen")
                        }
                    }
                }
            }
        },
        dismissButton = {
            when (workflowState) {
                0 -> {
                    TextButton(
                        onClick = { 
                            nfcReader.stopScanning()
                            onDismiss() 
                        },
                        enabled = true
                    ) {
                        Text("Abbrechen")
                    }
                }
                1 -> {
                    TextButton(
                        onClick = {
                            workflowState = 0
                        },
                        enabled = true
                    ) {
                        Text("Zurück")
                    }
                }
                2 -> null // Abbrechen-Button ist bereits im confirmButton
            }
        }
    )
    
    // Stoppe NFC beim Schließen
    DisposableEffect(Unit) {
        onDispose {
            nfcReader.stopScanning()
        }
    }
}
