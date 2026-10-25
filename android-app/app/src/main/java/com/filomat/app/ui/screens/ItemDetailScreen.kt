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
import com.filomat.app.data.sync.StoreState
import com.filomat.app.data.sync.Spool
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.ScanButton
import com.filomat.app.ui.components.rememberNfcReader
import com.filomat.app.ui.components.SpoolPlacePicker
import com.filomat.app.ui.components.TransmissionField
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
                state = current,
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
    state: StoreState,
    connected: Boolean,
    store: ContainerStore,
    modifier: Modifier
) {
    val scope = rememberCoroutineScope()
    var netWeight by remember(item.id) { mutableStateOf(GramText.format(item.netWeight)) }
    var spoolWeight by remember(item.id) { mutableStateOf(GramText.format(item.spoolWeight)) }
    var spoolsInStock by remember(item.id) { mutableStateOf(item.spoolsInStock ?: 0) }
    var transmission by remember(item.id) { mutableStateOf(GramText.format(item.transmissionDistance)) }
    var consumption by remember(item.id) { mutableStateOf<List<ConsumptionEntry>>(emptyList()) }
    var isBusy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var showingEdit by remember { mutableStateOf(false) }
    var spoolToEdit by remember { mutableStateOf<Spool?>(null) }
    val reader = rememberNfcReader()
    var isAssigningTag by remember { mutableStateOf(false) }
    var tagError by remember { mutableStateOf<String?>(null) }
    // Gescanntes Tag, das noch einem anderen Filament gehört; wartet auf Bestätigung
    var pendingTag by remember { mutableStateOf<Pair<String, Item>?>(null) }

    fun changeTags(operation: suspend () -> Unit) {
        isAssigningTag = true
        scope.launch {
            try {
                operation()
            } catch (e: Exception) {
                tagError = ContainerStore.describe(e)
            }
            isAssigningTag = false
        }
    }

    fun addTag(tag: String) = changeTags { store.addTag(tag, item.id) }

    fun prepareTagAssignment(scanned: String) {
        val tag = scanned.trim()
        tagError = null
        val previous = state.findItemByTag(tag)
        when {
            tag.isEmpty() -> tagError = "Ungültiger Tag."
            item.hasTag(tag) -> tagError = "Dieses Tag gehört schon zu diesem Filament."
            state.findContainerByTag(tag) != null ->
                tagError = "Dieses Tag gehört zum Container '${state.findContainerByTag(tag)?.name}'."
            previous == null -> addTag(tag)
            previous.allTags.size <= 1 && state.spoolsOf(previous.id).isNotEmpty() ->
                tagError = "Das Tag gehört zu „${previous.getDisplayTitle()}“, das angebrochene Spulen hat."
            else -> pendingTag = tag to previous
        }
    }

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
        spoolsInStock = spoolsInStock,
        transmissionDistance = GramText.parse(transmission)
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
                }
            }
        }

        item {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Tags", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                        if (isAssigningTag) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                    }
                    if (item.allTags.isEmpty()) Text("Noch kein Tag", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    item.allTags.forEach { tag ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(tag, modifier = Modifier.weight(1f))
                            if (item.allTags.size > 1) {
                                TextButton(enabled = !isAssigningTag, onClick = { changeTags { store.removeTag(tag, item.id) } }) {
                                    Text("Entfernen")
                                }
                            }
                        }
                    }
                    ScanButton(reader, "Plättchen hinzufügen") { tag -> prepareTagAssignment(tag.tagId) }
                    Text(
                        "Jedes dieser Tags führt beim Scannen zu diesem Filament, z. B. Musterplättchen an Boxen und Druckern. Gehört ein gescanntes Tag noch einem anderen Eintrag, wird es dort entfernt; war es dessen einziges, wird der Eintrag gelöscht.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    tagError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
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
                    TransmissionField(transmission) { transmission = it }
                    Text(
                        "Das Leergewicht kommt, falls bekannt, aus der SpoolmanDB für die Marke. Reicht eine Spule für einen Druck nicht, wird automatisch eine aus dem Vorrat angebrochen. Die Lichtdurchlässigkeit ist der TD-Wert in mm, wie ihn z. B. ein TD-1 misst.",
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
                        Text(spool.locationText(state.printers, state.containers), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
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

    pendingTag?.let { (tag, previous) ->
        AlertDialog(
            onDismissRequest = { pendingTag = null },
            title = { Text("Plättchen übernehmen?") },
            text = {
                Text(
                    if (previous.allTags.size > 1) "Das Tag $tag gehört zu „${previous.getDisplayTitle()}“ und wird dort entfernt."
                    else "Das Tag $tag gehört zu „${previous.getDisplayTitle()}“. Dieser Eintrag hat keine angebrochenen Spulen und wird gelöscht."
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    pendingTag = null
                    addTag(tag)
                }) { Text(if (previous.allTags.size > 1) "Übernehmen" else "Löschen und übernehmen") }
            },
            dismissButton = {
                TextButton(onClick = { pendingTag = null }) { Text("Abbrechen") }
            }
        )
    }

    if (showingEdit) {
        EditFilamentDialog(item = item, containerStore = store, onDismiss = { showingEdit = false })
    }

    spoolToEdit?.let { spool ->
        SpoolEditDialog(
            spool = spool,
            emptySpoolWeight = GramText.parse(spoolWeight),
            state = state,
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

/** Angebrochene Spule wiegen, einlegen oder als leer markieren. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SpoolEditDialog(
    spool: Spool,
    emptySpoolWeight: Double?,
    state: StoreState,
    store: ContainerStore,
    onDismiss: () -> Unit
) {
    val scope = rememberCoroutineScope()
    var remaining by remember { mutableStateOf(GramText.format(spool.remainingWeight)) }
    var weighed by remember { mutableStateOf("") }
    var emptySpool by remember { mutableStateOf(GramText.format(emptySpoolWeight)) }
    var location by remember { mutableStateOf(spool.place) }
    var isSaving by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

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
                SpoolPlacePicker("Steckt in", state, location) { location = it }
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
                        spool.movedTo(location).copy(remainingWeight = GramText.parse(remaining))
                    )
                }
            }) { Text("Speichern") }
        },
        dismissButton = { TextButton(enabled = !isSaving, onClick = onDismiss) { Text("Abbrechen") } }
    )
}
