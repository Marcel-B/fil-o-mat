package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.sync.StoreState
import com.filomat.app.nfc.NFCReader
import com.filomat.app.nfc.TagReading
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.GramText

/** Einen Tag einfach auslesen und alles anzeigen, was darauf steht; in der App ändert sich nichts. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TagReadScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val nfcReader = remember { NFCReader(context as android.app.Activity) }
    val isScanning by nfcReader.isScanning.collectAsState()
    val nfcError by nfcReader.errorMessage.collectAsState()
    var reading by remember { mutableStateOf<TagReading?>(null) }

    DisposableEffect(Unit) { onDispose { nfcReader.stopScanning() } }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Filament-Tag lesen") },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Zurück")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            if (nfcReader.isAvailable) {
                Button(
                    onClick = { nfcReader.readTag { reading = it } },
                    enabled = !isScanning,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    if (isScanning) {
                        CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
                        Spacer(Modifier.width(8.dp))
                        Text("Tag an das Handy halten …")
                    } else {
                        Text(if (reading == null) "Tag lesen" else "Nächsten Tag lesen")
                    }
                }
                if (isScanning) {
                    TextButton(onClick = { nfcReader.stopScanning() }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                        Text("Abbrechen")
                    }
                }
            } else {
                Text("NFC ist auf diesem Gerät nicht verfügbar oder nicht aktiviert.", color = MaterialTheme.colorScheme.error)
            }
            nfcError?.let { Text(it, color = MaterialTheme.colorScheme.error) }

            val current = reading
            if (current == null) {
                Text(
                    "Zeigt alles an, was auf einem Filament- oder Container-Tag steht, auch Felder anderer Programme. In der App wird dabei nichts geändert.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                TagReadingContent(current, state, onOpenItem = { navController.navigate("item/$it") })
            }
        }
    }
}

@Composable
private fun TagReadingContent(reading: TagReading, state: StoreState, onOpenItem: (String) -> Unit) {
    val contents = reading.contents
    val item = reading.ids.firstNotNullOfOrNull { state.findItemByTag(it) }
    val container = reading.ids.firstNotNullOfOrNull { state.findContainerByTag(it) }

    reading.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }

    // Filament: Kopf mit Farbe und Titel
    val data = contents?.itemData
    if (data != null && (data.type != null || data.brand != null)) {
        Card(Modifier.fillMaxWidth()) {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                ColorDot(data.color_hex, 48.dp)
                Column {
                    val title = listOfNotNull(data.brand, data.type, data.variant).joinToString(" ")
                    Text(title.ifEmpty { "Filament" }, style = MaterialTheme.typography.titleMedium)
                    (data.color ?: data.color_hex?.let { "#" + it.removePrefix("#") })?.let {
                        Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }

    Section("In der App") {
        when {
            item != null -> {
                InfoRow("Filament", item.getDisplayTitle())
                InfoRow("Container", state.container(item.containerId)?.name ?: "Lose")
                item.spoolsInStock?.takeIf { it > 0 }?.let { InfoRow("Vorrat", if (it == 1) "1 Spule" else "$it Spulen") }
                item.transmissionText()?.let { InfoRow("Lichtdurchlässigkeit (TD)", it) }
                state.spoolsOf(item.id).forEach { spool ->
                    InfoRow("Spule #${spool.number}", listOfNotNull(spool.locationText(state.printers, state.containers), spool.remainingWeight?.let(GramText::label)).joinToString(" · "))
                }
                OutlinedButton(onClick = { onOpenItem(item.id) }, modifier = Modifier.fillMaxWidth()) {
                    Text("Filament bearbeiten")
                }
            }
            container != null -> {
                InfoRow("Container", container.name)
                InfoRow("Spulen darin", "${state.spoolCount(container.id)}")
            }
            else -> Text("Dieser Tag ist in der App noch nicht bekannt.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }

    if (contents != null) {
        Section("Daten auf dem Tag") {
            contents.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            contents.fields.forEach { field ->
                InfoRow(field.label, field.value)
            }
        }
    }

    Section("Chip") {
        InfoRow("UID", reading.uid)
        reading.ndefType?.let { InfoRow("NDEF-Typ", it) }
        if (reading.ndefCapacity != null) {
            InfoRow("Belegt", "${reading.ndefUsed ?: 0} von ${reading.ndefCapacity} Bytes")
        }
        reading.isWritable?.let { InfoRow("Beschreibbar", if (it) "Ja" else "Nein (gesperrt)") }
        InfoRow("Technik", reading.technologies.joinToString(", "))
    }

    if (reading.records.isNotEmpty()) {
        Section("NDEF-Records") {
            reading.records.forEachIndexed { index, record ->
                Text("${index + 1}. ${record.format} · ${record.type} · ${record.sizeBytes} Bytes", style = MaterialTheme.typography.labelLarge)
                record.text?.let {
                    SelectionContainer {
                        Text(it, fontFamily = FontFamily.Monospace, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }
    } else if (reading.error == null) {
        Text("Auf dem Tag stehen keine Daten.", color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun Section(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium)
            content()
        }
    }
}
