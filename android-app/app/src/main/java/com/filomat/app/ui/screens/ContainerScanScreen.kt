package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.StoreState
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.ItemHeader
import com.filomat.app.ui.components.ScanButton
import com.filomat.app.ui.components.SectionCard
import com.filomat.app.ui.components.rememberNfcReader

/** Container scannen und sehen, was darin liegt: Sorten, angebrochene Spulen bzw. bei einem Drucker die Spulen je Kopf. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContainerScanScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val reader = rememberNfcReader()
    var containerId by remember { mutableStateOf<String?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Container ansehen") },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Zurück")
                    }
                }
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            item {
                ScanButton(reader, if (containerId == null) "Container scannen" else "Nächsten Container scannen") { tag ->
                    val tagId = tag.tagId.trim()
                    val container = state.findContainerByTag(tagId)
                    when {
                        container != null -> {
                            containerId = container.id
                            errorMessage = null
                        }
                        state.findItemByTag(tagId) != null ->
                            errorMessage = "Das ist ein Filament-Tag. Für Filamente gibt es „Filament-Tag lesen“."
                        else -> errorMessage = "Dieser Container ist in Fil-o-mat noch nicht angelegt."
                    }
                }
                errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }

            val container = containerId?.let { state.container(it) }
            if (container != null) {
                val printer = state.printerOwning(container.id)
                item {
                    if (printer != null) {
                        PrinterContent(printer, state)
                    } else {
                        ContainerContent(container, state, onOpen = { navController.navigate("container/${container.id}") })
                    }
                }
            }
        }
    }
}

@Composable
private fun PrinterContent(printer: Printer, state: StoreState) {
    SectionCard("Drucker ${printer.name}") {
        for (tool in 0 until printer.toolCount) {
            val label = if (printer.toolCount > 1) printer.placeName(tool) else "Eingelegt"
            val spool = state.spoolIn(printer.id, tool)
            InfoRow(label, spool?.let { spoolName(it, state) + (it.remainingWeight?.let { g -> " · " + GramText.label(g) } ?: "") } ?: "frei")
        }
    }
}

@Composable
private fun ContainerContent(container: Container, state: StoreState, onOpen: () -> Unit) {
    val items = state.itemsIn(container.id)
    val spools = state.spoolsIn(container.id)
    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionCard(container.name) {
            if (container.description.isNotEmpty()) {
                Text(container.description, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            OutlinedButton(onClick = onOpen, modifier = Modifier.fillMaxWidth()) { Text("Container öffnen") }
        }
        SectionCard("Sorten (${items.size})") {
            if (items.isEmpty()) {
                Text("Keine Sorten in diesem Container", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            items.forEach { item -> ItemHeader(item, item.spoolsInStock?.let { "Vorrat: $it" }) }
        }
        if (spools.isNotEmpty()) {
            OpenedSpoolsCard(spools, state)
        }
    }
}

/** Angebrochene Spulen, die in einem Container liegen. */
@Composable
fun OpenedSpoolsCard(spools: List<Spool>, state: StoreState) {
    SectionCard("Angebrochene Spulen (${spools.size})") {
        spools.forEach { spool ->
            InfoRow(spoolName(spool, state), spool.remainingWeight?.let(GramText::label) ?: "Rest unbekannt")
        }
    }
}

/** "Prusament PLA Galaxy Black #3" */
fun spoolName(spool: Spool, state: StoreState): String =
    "${state.item(spool.itemId)?.getDisplayTitle() ?: "Filament"} #${spool.number}"
