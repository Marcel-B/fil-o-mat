package com.filomat.app.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import com.filomat.app.data.ItemJsonData
import com.filomat.app.data.PlaceAction
import com.filomat.app.data.PlaceFlow
import com.filomat.app.data.PlaceStep
import com.filomat.app.data.PlaceTarget
import com.filomat.app.data.SpoolChoice
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.StoreState
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.ItemHeader
import com.filomat.app.ui.components.ScanButton
import com.filomat.app.ui.components.SectionCard
import com.filomat.app.ui.components.rememberNfcReader
import kotlinx.coroutines.launch

/** "Einlagern" bzw. "In Drucker einlegen" in drei Schritten: Filament, Spule, Ziel. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PlaceFlowScreen(target: PlaceTarget, navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val sync by store.sync.collectAsState()
    val reader = rememberNfcReader()
    val scope = rememberCoroutineScope()
    var flow by remember { mutableStateOf(PlaceFlow(target)) }
    var isWorking by remember { mutableStateOf(false) }
    var choosingItem by remember { mutableStateOf(false) }

    fun perform(action: PlaceAction) {
        isWorking = true
        scope.launch {
            try {
                val message = when (action) {
                    is PlaceAction.MoveItem -> {
                        store.addItemToContainer(action.item.id, action.container.id)
                        "${action.item.getDisplayTitle()} liegt jetzt in „${action.container.name}“."
                    }
                    is PlaceAction.MoveSpool -> {
                        store.placeSpool(action.spool, action.place)
                        val where = action.place.text(state.printers, state.containers)
                        if (action.place.printerId != null) "Spule #${action.spool.number} von ${action.item.getDisplayTitle()} steckt jetzt in $where."
                        else "Spule #${action.spool.number} von ${action.item.getDisplayTitle()} liegt jetzt in „$where“."
                    }
                    is PlaceAction.OpenSpool -> {
                        val spool = store.openSpool(action.item.id, action.place)
                        val number = spool?.let { " #${it.number}" } ?: ""
                        val where = action.place.text(state.printers, state.containers)
                        if (action.place.printerId != null) "Spule$number von ${action.item.getDisplayTitle()} angebrochen und in $where eingelegt. Schreib die Nummer auf die Spule."
                        else "Spule$number von ${action.item.getDisplayTitle()} angebrochen und in „$where“ eingelagert. Schreib die Nummer auf die Spule."
                    }
                }
                flow = flow.reset(done = message)
            } catch (e: Exception) {
                flow = flow.failed(ContainerStore.describe(e))
            }
            isWorking = false
        }
    }

    if (choosingItem) {
        ItemPickerDialog(
            items = state.items,
            suggested = flow.unknownData,
            onDismiss = { choosingItem = false },
            onPick = { picked ->
                choosingItem = false
                val tag = flow.unknownTag ?: return@ItemPickerDialog
                scope.launch {
                    flow = try {
                        store.addTag(tag, picked.id)
                        val current = store.current()
                        flow.withItem(current.item(picked.id) ?: picked, current, sync.connected)
                    } catch (e: Exception) {
                        flow.failed(ContainerStore.describe(e))
                    }
                }
            }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(flow.title) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Zurück")
                    }
                },
                actions = {
                    if (flow.step != PlaceStep.SCAN_FILAMENT || flow.unknownTag != null) {
                        TextButton(onClick = { flow = flow.reset() }) { Text("Neu beginnen") }
                    }
                }
            )
        },
        bottomBar = {
            if (flow.step == PlaceStep.CHOOSE_TARGET) {
                Surface(tonalElevation = 3.dp) {
                    Button(
                        enabled = !isWorking && flow.canPerform(state),
                        onClick = { flow.action(state)?.let(::perform) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(16.dp)
                    ) {
                        if (isWorking) {
                            CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.width(8.dp))
                        }
                        Text(flow.actionTitle)
                    }
                }
            }
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            flow.done?.let { done ->
                item {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Icon(Icons.Default.CheckCircle, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Text(done)
                    }
                }
            }

            // Schritt 1: Filament
            item {
                SectionCard("1. Filament") {
                    val item = flow.currentItem(state)
                    val unknown = flow.unknownTag
                    if (item != null) {
                        ItemHeader(item, stockText(item, state))
                    } else if (unknown != null) {
                        Text("Dieses Plättchen ($unknown) kennt die App noch nicht.")
                        Button(onClick = { choosingItem = true }, modifier = Modifier.fillMaxWidth()) {
                            Text("Zu vorhandenem Filament hinzufügen")
                        }
                        OutlinedButton(
                            onClick = {
                                val (next, newItem) = flow.withNewItem(state, sync.connected)
                                newItem?.let { scope.launch { store.addItem(it) } }
                                flow = next
                            },
                            modifier = Modifier.fillMaxWidth()
                        ) { Text("Als neues Filament anlegen") }
                    } else {
                        Text(
                            if (target == PlaceTarget.CONTAINER) "Scanne das Musterplättchen des Filaments, das eingelagert wird."
                            else "Scanne das Musterplättchen des Filaments, das in den Drucker kommt.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        ScanButton(reader, "Filament scannen") { tag ->
                            val next = flow.withFilament(tag.tagId, tag.itemData, state, sync.connected)
                            flow = if (tag.parseError != null && next.error == null) next.failed(tag.parseError) else next
                        }
                    }
                }
            }

            // Schritt 2: welche Spule
            if (flow.step != PlaceStep.SCAN_FILAMENT) {
                item {
                    SectionCard(if (target == PlaceTarget.CONTAINER) "2. Was kommt in den Container?" else "2. Was kommt in den Drucker?") {
                        val options = flow.choices(state, sync.connected)
                        if (options.isEmpty()) {
                            Text("Für Spulen in Druckern muss das Backend verbunden sein.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        options.forEach { choice ->
                            SelectableRow(
                                title = choiceTitle(choice, state),
                                detail = choiceDetail(choice, flow.currentItem(state), state),
                                selected = flow.choice == choice
                            ) { flow = flow.choose(choice, state) }
                        }
                    }
                }
            }

            // Schritt 3: Ziel
            if (flow.step == PlaceStep.CHOOSE_TARGET) {
                item {
                    when (target) {
                        PlaceTarget.CONTAINER -> ContainerTarget(flow, state, reader) { flow = it }
                        PlaceTarget.PRINTER -> PrinterTarget(flow, state) { flow = it }
                    }
                }
            }

            flow.error?.let { error ->
                item { Text(error, color = MaterialTheme.colorScheme.error) }
            }
        }
    }
}

@Composable
private fun ContainerTarget(flow: PlaceFlow, state: StoreState, reader: com.filomat.app.nfc.NFCReader, onChange: (PlaceFlow) -> Unit) {
    var menuOpen by remember { mutableStateOf(false) }
    SectionCard("3. Container") {
        flow.containerId?.let { state.container(it) }?.let { container ->
            InfoRow("Container", container.name)
            if (flow.isAlreadyThere(state)) {
                Text("Liegt schon in diesem Container.", color = MaterialTheme.colorScheme.tertiary)
            }
        }
        ScanButton(reader, if (flow.containerId == null) "Container scannen" else "Anderen Container scannen") { tag ->
            onChange(flow.withTargetTag(tag.tagId, state))
        }
        Box {
            OutlinedButton(onClick = { menuOpen = true }, modifier = Modifier.fillMaxWidth()) { Text("Aus der Liste wählen") }
            DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                state.storageContainers().forEach { container ->
                    DropdownMenuItem(text = { Text(container.name) }, onClick = {
                        menuOpen = false
                        onChange(flow.selectContainer(container, state))
                    })
                }
            }
        }
    }
}

@Composable
private fun PrinterTarget(flow: PlaceFlow, state: StoreState, onChange: (PlaceFlow) -> Unit) {
    val displaced = flow.displacedSpool(state)
    val footer = when {
        displaced != null -> "Dort steckt gerade ${spoolName(displaced, state)}; sie kommt ins Lager."
        flow.isAlreadyThere(state) -> "Die Spule steckt schon dort."
        else -> null
    }
    SectionCard("3. Drucker und Kopf", footer = footer) {
        if (state.printers.isEmpty()) {
            Text("Noch keine Drucker angelegt (Tab „Drucker“).", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        state.printers.forEach { printer ->
            for (tool in 0 until printer.toolCount) {
                val place = SpoolPlace.printer(printer.id, tool)
                SelectableRow(
                    title = printer.placeName(tool),
                    detail = state.spoolIn(printer.id, tool)?.let { spoolName(it, state) } ?: "frei",
                    selected = flow.printerPlace == place
                ) { onChange(flow.selectPlace(place)) }
            }
        }
    }
}

@Composable
private fun SelectableRow(title: String, detail: String, selected: Boolean, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f)) {
            Text(title)
            if (detail.isNotEmpty()) {
                Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        if (selected) Icon(Icons.Default.Check, contentDescription = "Gewählt", tint = MaterialTheme.colorScheme.primary)
    }
}

private fun choiceTitle(choice: SpoolChoice, state: StoreState): String = when (choice) {
    SpoolChoice.Stock -> "Sorte mit vollen Spulen"
    is SpoolChoice.Opened -> "Angebrochene Spule #${state.spool(choice.spoolId)?.number ?: 0}"
    SpoolChoice.NewSpool -> "Neue Spule anbrechen"
}

private fun choiceDetail(choice: SpoolChoice, item: Item?, state: StoreState): String = when (choice) {
    SpoolChoice.Stock -> {
        val place = item?.containerId?.let { state.container(it)?.name } ?: "kein Container"
        "Musterplättchen und Vorrat · bisher: $place"
    }
    is SpoolChoice.Opened -> state.spool(choice.spoolId)?.let { spool ->
        val rest = spool.remainingWeight?.let(GramText::label) ?: "Rest unbekannt"
        "$rest · ${spool.locationText(state.printers, state.containers)}"
    } ?: ""
    SpoolChoice.NewSpool -> {
        val stock = item?.spoolsInStock ?: 0
        if (stock > 0) "Aus dem Vorrat ($stock ${if (stock == 1) "Spule" else "Spulen"})" else "Im Vorrat ist keine Spule mehr eingetragen"
    }
}

private fun stockText(item: Item, state: StoreState): String {
    val parts = mutableListOf<String>()
    item.spoolsInStock?.let { parts += "Vorrat: $it" }
    val opened = state.spoolsOf(item.id).size
    if (opened > 0) parts += "$opened angebrochen"
    return if (parts.isEmpty()) item.rfidTag else parts.joinToString(" · ")
}

/** Filament auswählen; Filamente mit derselben Marke und demselben Typ wie auf dem Tag stehen oben. */
@Composable
fun ItemPickerDialog(items: List<Item>, suggested: ItemJsonData?, onDismiss: () -> Unit, onPick: (Item) -> Unit) {
    var query by remember { mutableStateOf("") }
    fun isSuggested(item: Item): Boolean {
        val brand = suggested?.brand?.lowercase() ?: return false
        val type = suggested.type?.lowercase() ?: return false
        return item.itemData?.brand?.lowercase() == brand && item.itemData?.type?.lowercase() == type
    }
    val text = query.trim().lowercase()
    val shown = items
        .filter { text.isEmpty() || it.getDisplayTitle().lowercase().contains(text) || it.name.lowercase().contains(text) }
        .sortedWith(compareBy<Item>({ !isSuggested(it) }, { it.getDisplayTitle().lowercase() }))
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Filament wählen") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(value = query, onValueChange = { query = it }, label = { Text("Suchen") }, singleLine = true,
                    modifier = Modifier.fillMaxWidth())
                LazyColumn(Modifier.heightIn(max = 360.dp)) {
                    items(shown, key = { it.id }) { item ->
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .clickable { onPick(item) }
                                .padding(vertical = 10.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(12.dp)
                        ) {
                            ColorDot(item.itemData?.color_hex, 24.dp)
                            Text(item.getDisplayTitle())
                        }
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } }
    )
}
