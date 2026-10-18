package com.filomat.app.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Remove
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import com.filomat.app.data.sync.ConsumptionEntry
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.Spool
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.GramText
import kotlinx.coroutines.launch
import java.text.DateFormat

/** Eine Filamentsorte: Tag-Daten, Gewichte, Vorrat, angebrochene Spulen und Verbrauch (wie das Formular der iOS-App). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ItemDetailScreen(itemId: String, navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = null)
    val sync by store.sync.collectAsState()
    val item = state?.item(itemId)

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(item?.getDisplayTitle() ?: "Filament") },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Zurück")
                    }
                }
            )
        }
    ) { padding ->
        val current = state
        if (current == null) {
            Box(Modifier.fillMaxSize().padding(padding))
        } else if (item == null) {
            Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
                Text("Filament nicht gefunden")
            }
        } else {
            ItemDetailContent(
                item = item,
                containerName = current.container(item.containerId)?.name ?: "Lose",
                spools = current.spoolsOf(item.id),
                printers = current.printers,
                connected = sync.connected,
                store = store,
                modifier = Modifier.padding(padding)
            )
        }
    }
}

@Composable
private fun ItemDetailContent(
    item: Item,
    containerName: String,
    spools: List<Spool>,
    printers: List<Printer>,
    connected: Boolean,
    store: ContainerStore,
    modifier: Modifier
) {
    val scope = rememberCoroutineScope()
    var netWeight by remember(item.id) { mutableStateOf(GramText.format(item.netWeight)) }
    var spoolWeight by remember(item.id) { mutableStateOf(GramText.format(item.spoolWeight)) }
    var spoolsInStock by remember(item.id) { mutableStateOf(item.spoolsInStock ?: 0) }
    var consumption by remember(item.id) { mutableStateOf<List<ConsumptionEntry>>(emptyList()) }
    var isBusy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var showingEdit by remember { mutableStateOf(false) }
    var spoolToEdit by remember { mutableStateOf<Spool?>(null) }

    // Nach einem Abgleich (z. B. Vorrat nach dem Anbrechen) den neuen Stand übernehmen
    LaunchedEffect(item.spoolsInStock) { spoolsInStock = item.spoolsInStock ?: 0 }
    LaunchedEffect(item.id, connected) {
        consumption = store.consumption(item.id)
        // Leergewicht der Marke aus der SpoolmanDB vorschlagen, wenn noch keins eingetragen ist
        if (spoolWeight.isEmpty()) {
            val suggested = store.suggestedSpoolWeight(item.itemData?.brand ?: "")
            if (suggested != null && spoolWeight.isEmpty()) spoolWeight = GramText.format(suggested)
        }
    }

    fun withWeights(): Item = item.copy(
        netWeight = GramText.parse(netWeight),
        spoolWeight = GramText.parse(spoolWeight),
        spoolsInStock = spoolsInStock
    )

    LazyColumn(
        modifier = modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        item {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        ColorDot(item.itemData?.color_hex, 40.dp)
                        Column(Modifier.weight(1f)) {
                            Text(item.getDisplayTitle(), style = MaterialTheme.typography.titleMedium)
                            item.colorLabel()?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                        }
                        IconButton(onClick = { showingEdit = true }) {
                            Icon(Icons.Default.Edit, contentDescription = "Filament-Daten bearbeiten")
                        }
                    }
                    item.itemData?.type?.let { InfoRow("Typ", it) }
                    item.itemData?.brand?.let { InfoRow("Marke", it) }
                    item.itemData?.variant?.let { InfoRow("Variante", it) }
                    val data = item.itemData
                    if (data?.min_temp != null && data.max_temp != null) InfoRow("Temperatur", "${data.min_temp}°C - ${data.max_temp}°C")
                    if (data?.bed_min_temp != null && data.bed_max_temp != null) InfoRow("Bett-Temp", "${data.bed_min_temp}°C - ${data.bed_max_temp}°C")
                    InfoRow("Container", containerName)
                    InfoRow("RFID-Tag", item.rfidTag)
                }
            }
        }

        item {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Sorte", style = MaterialTheme.typography.titleMedium)
                    GramField("Füllgewicht neue Spule (g)", netWeight) { netWeight = it }
                    GramField("Leere Spule (g)", spoolWeight) { spoolWeight = it }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Volle Spulen im Vorrat: $spoolsInStock", modifier = Modifier.weight(1f))
                        IconButton(onClick = { if (spoolsInStock > 0) spoolsInStock-- }) {
                            Icon(Icons.Default.Remove, contentDescription = "Eine weniger")
                        }
                        IconButton(onClick = { if (spoolsInStock < 99) spoolsInStock++ }) {
                            Icon(Icons.Default.Add, contentDescription = "Eine mehr")
                        }
                    }
                    Text(
                        "Das Leergewicht kommt, falls bekannt, aus der SpoolmanDB für die Marke. Reicht eine Spule für einen Druck nicht, wird automatisch eine aus dem Vorrat angebrochen.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Button(
                        enabled = !isBusy,
                        onClick = {
                            scope.launch {
                                store.updateItem(withWeights(), sendWeights = true)
                                message = "Gespeichert."
                            }
                        },
                        modifier = Modifier.fillMaxWidth()
                    ) { Text("Speichern") }
                    message?.let { Text(it, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodySmall) }
                }
            }
        }

        if (connected) {
            item {
                Text("Angebrochene Spulen", style = MaterialTheme.typography.titleMedium)
            }
            items(spools, key = { it.id }) { spool ->
                Card(
                    Modifier
                        .fillMaxWidth()
                        .clickable { spoolToEdit = spool }
                ) {
                    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text("#${spool.number}", style = MaterialTheme.typography.titleMedium)
                        Spacer(Modifier.width(12.dp))
                        Text(spool.locationText(printers), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                        spool.remainingWeight?.let { Text(GramText.label(it)) }
                    }
                }
            }
            item {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    OutlinedButton(
                        enabled = !isBusy,
                        onClick = {
                            isBusy = true
                            errorMessage = null
                            scope.launch {
                                try {
                                    store.openSpool(withWeights())
                                } catch (e: Exception) {
                                    errorMessage = ContainerStore.describe(e)
                                }
                                isBusy = false
                            }
                        },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        if (isBusy) {
                            CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.width(8.dp))
                        }
                        Text("Neue Spule anbrechen")
                    }
                    Text(
                        "Schreib die Nummer mit Stift auf die Spule. Gedruckter Verbrauch wird von der Spule abgezogen, die im Drucker an diesem Kopf steckt.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                }
            }
        }

        if (consumption.isNotEmpty()) {
            item { Text("Verbrauch", style = MaterialTheme.typography.titleMedium) }
            items(consumption, key = { it.id }) { entry ->
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(entry.jobName, maxLines = 1)
                        entry.endedDate?.let {
                            Text(
                                DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT).format(it),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                    Text(String.format("%.1f g", entry.grams))
                }
            }
        }
    }

    if (showingEdit) {
        EditFilamentDialog(item = item, containerStore = store, onDismiss = { showingEdit = false })
    }

    spoolToEdit?.let { spool ->
        SpoolEditDialog(
            spool = spool,
            emptySpoolWeight = GramText.parse(spoolWeight),
            printers = printers,
            store = store,
            onDismiss = { spoolToEdit = null }
        )
    }
}

@Composable
private fun GramField(label: String, value: String, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = { text -> if (text.all { it.isDigit() || it == ',' || it == '.' }) onChange(text) },
        label = { Text(label) },
        placeholder = { Text("z. B. 1000") },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
        modifier = Modifier.fillMaxWidth()
    )
}

/** Platz einer Spule: Lager oder ein Kopf eines Druckers. */
private data class SpoolLocation(val printerId: String?, val tool: Int?)

/** Angebrochene Spule wiegen, einlegen oder als leer markieren. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SpoolEditDialog(
    spool: Spool,
    emptySpoolWeight: Double?,
    printers: List<Printer>,
    store: ContainerStore,
    onDismiss: () -> Unit
) {
    val scope = rememberCoroutineScope()
    var remaining by remember { mutableStateOf(GramText.format(spool.remainingWeight)) }
    var weighed by remember { mutableStateOf("") }
    var emptySpool by remember { mutableStateOf(GramText.format(emptySpoolWeight)) }
    var location by remember {
        mutableStateOf(SpoolLocation(spool.printerId, if (spool.printerId == null) null else spool.tool ?: 0))
    }
    var locationExpanded by remember { mutableStateOf(false) }
    var isSaving by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val locations = remember(printers) {
        listOf(SpoolLocation(null, null) to "Lager") + printers.flatMap { printer ->
            (0 until printer.toolCount).map { tool -> SpoolLocation(printer.id, tool) to printer.placeName(tool) }
        }
    }
    val locationName = locations.firstOrNull { it.first == location }?.second ?: spool.locationText(printers)

    // Das Restgewicht ergibt sich aus dem gewogenen Gewicht minus leerer Spule
    fun updateFromScale() {
        val gross = GramText.parse(weighed) ?: return
        val empty = GramText.parse(emptySpool) ?: return
        remaining = GramText.format(maxOf(0.0, gross - empty))
    }

    fun perform(action: suspend () -> Unit) {
        isSaving = true
        errorMessage = null
        scope.launch {
            try {
                action()
                onDismiss()
            } catch (e: Exception) {
                errorMessage = ContainerStore.describe(e)
            }
            isSaving = false
        }
    }

    AlertDialog(
        onDismissRequest = { if (!isSaving) onDismiss() },
        title = { Text("Spule #${spool.number}") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                GramField("Gewogen mit Spule (g)", weighed) { weighed = it; updateFromScale() }
                GramField("Leere Spule (g)", emptySpool) { emptySpool = it; updateFromScale() }
                GramField("Restgewicht (g)", remaining) { remaining = it }
                ExposedDropdownMenuBox(expanded = locationExpanded, onExpandedChange = { locationExpanded = it }) {
                    OutlinedTextField(
                        value = locationName,
                        onValueChange = {},
                        readOnly = true,
                        label = { Text("Steckt in") },
                        trailingIcon = { Icon(Icons.Default.ArrowDropDown, contentDescription = null) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .menuAnchor()
                    )
                    ExposedDropdownMenu(expanded = locationExpanded, onDismissRequest = { locationExpanded = false }) {
                        locations.forEach { (option, name) ->
                            DropdownMenuItem(text = { Text(name) }, onClick = {
                                location = option
                                locationExpanded = false
                            })
                        }
                    }
                }
                if (isSaving) LinearProgressIndicator(Modifier.fillMaxWidth())
                errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                TextButton(
                    enabled = !isSaving,
                    onClick = { perform { store.markSpoolEmpty(spool.id) } }
                ) { Text("Spule ist leer", color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(enabled = !isSaving, onClick = {
                perform {
                    store.updateSpool(
                        spool.copy(
                            remainingWeight = GramText.parse(remaining),
                            printerId = location.printerId,
                            tool = location.tool
                        )
                    )
                }
            }) { Text("Speichern") }
        },
        dismissButton = { TextButton(enabled = !isSaving, onClick = onDismiss) { Text("Abbrechen") } }
    )
}
