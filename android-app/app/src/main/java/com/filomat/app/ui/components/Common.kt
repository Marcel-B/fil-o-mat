package com.filomat.app.ui.components

import android.graphics.Color as AndroidColor
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import android.app.Activity
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.TextButton
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.StoreState
import com.filomat.app.nfc.NFCReader
import com.filomat.app.nfc.ScannedTag
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Print
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import androidx.navigation.NavGraph.Companion.findStartDestination

/** Hauptbereiche der App, wie die Tabs der iOS-App. "Tag lesen" steht wie dort im Scannen-Tab. */
enum class MainTab(val route: String, val label: String, val icon: ImageVector) {
    CONTAINERS("containers", "Container", Icons.Default.Archive),
    SCAN("scan", "Scannen", Icons.Default.QrCodeScanner),
    ITEMS("items", "Filamente", Icons.Default.Search),
    PRINTERS("printers", "Drucker", Icons.Default.Print)
}

@Composable
fun MainNavigationBar(navController: NavController, current: MainTab) {
    NavigationBar {
        MainTab.entries.forEach { tab ->
            NavigationBarItem(
                selected = tab == current,
                onClick = {
                    if (tab != current) {
                        navController.navigate(tab.route) {
                            // Tabs nicht übereinanderstapeln
                            popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                            launchSingleTop = true
                            restoreState = true
                        }
                    }
                },
                icon = { Icon(tab.icon, contentDescription = null) },
                label = { Text(tab.label, maxLines = 1) }
            )
        }
    }
}

/** Farbe aus einem `color_hex`-Wert ("FF0000", "#FF0000" oder "#AARRGGBB"); `null` bei ungültiger Angabe. */
fun parseHexColor(raw: String?): Color? {
    val trimmed = raw?.trim()?.takeIf { it.isNotEmpty() } ?: return null
    val hex = trimmed.removePrefix("#")
    if (hex.length != 6 && hex.length != 8) return null
    return try {
        Color(AndroidColor.parseColor("#$hex"))
    } catch (e: IllegalArgumentException) {
        null
    }
}

/** Farbkreis; ohne gültige Farbe ein grauer Platzhalter mit "n/a". */
@Composable
fun ColorDot(hex: String?, size: Dp = 40.dp, showsPlaceholder: Boolean = true) {
    val color = remember(hex) { parseHexColor(hex) }
    if (color != null) {
        Box(
            modifier = Modifier
                .size(size)
                .background(color, CircleShape)
                .border(if (size >= 30.dp) 2.dp else 1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.5f), CircleShape)
        )
    } else if (showsPlaceholder) {
        Box(
            modifier = Modifier
                .size(size)
                .background(MaterialTheme.colorScheme.surfaceVariant, CircleShape),
            contentAlignment = Alignment.Center
        ) {
            Text("n/a", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Gramm-Angaben als Text (Komma oder Punkt als Dezimaltrenner). */
object GramText {
    fun parse(text: String): Double? = text.trim().replace(',', '.').toDoubleOrNull()

    fun format(value: Double?): String {
        value ?: return ""
        return if (value == Math.rint(value)) value.toLong().toString() else value.toString()
    }

    /** "432 g" */
    fun label(value: Double): String = "${Math.round(value)} g"
}

/** Kopfzeile einer Sorte: Farbe, Titel und eine Zeile darunter. */
@Composable
fun ItemHeader(item: Item, detail: String? = null, modifier: Modifier = Modifier) {
    Row(modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        ColorDot(item.itemData?.color_hex, 36.dp)
        Column(Modifier.weight(1f)) {
            Text(item.getDisplayTitle(), style = MaterialTheme.typography.titleMedium)
            detail?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
        }
    }
}

/**
 * Knopf, der einen Tag scannt und das Ergebnis weiterreicht; während des Scans lässt er sich abbrechen.
 * Ohne NFC wählt man stattdessen aus der Liste, falls [pick] gesetzt ist.
 */
@Composable
fun ScanButton(
    reader: NFCReader,
    title: String,
    modifier: Modifier = Modifier,
    pick: TagPick? = null,
    onTag: (ScannedTag) -> Unit
) {
    val isScanning by reader.isScanning.collectAsState()
    val error by reader.errorMessage.collectAsState()
    var showingPicker by remember { mutableStateOf(false) }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        if (!reader.isAvailable) {
            if (pick != null) {
                Button(onClick = { showingPicker = true }, modifier = Modifier.fillMaxWidth()) { Text(pick.buttonTitle) }
                Text(
                    "NFC ist aus oder fehlt, deshalb aus der Liste.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        } else {
            Button(onClick = { reader.scanTag(onTag) }, enabled = !isScanning, modifier = Modifier.fillMaxWidth()) {
                if (isScanning) {
                    CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(8.dp))
                    Text("Tag an das Handy halten …")
                } else {
                    Text(title)
                }
            }
        }
        if (isScanning) {
            TextButton(onClick = { reader.stopScanning() }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                Text("Abbrechen")
            }
        }
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
    }
    if (showingPicker && pick != null) {
        TagPickerDialog(pick, onDismiss = { showingPicker = false }) {
            showingPicker = false
            onTag(it)
        }
    }
}

/** Was auf Geräten ohne NFC statt eines Scans aus der Liste gewählt wird. */
enum class TagPick(val buttonTitle: String) {
    ITEM("Filament wählen"),
    CONTAINER("Container wählen")
}

/**
 * Ersatz für einen Scan ohne NFC: Sorte oder Container aus der Liste wählen. Liefert dasselbe wie ein Scan
 * des zugehörigen Tags, damit die Abläufe unverändert bleiben.
 */
@Composable
fun TagPickerDialog(
    kind: TagPick,
    excludedItemIds: Set<String> = emptySet(),
    onDismiss: () -> Unit,
    onPick: (ScannedTag) -> Unit
) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    var search by remember { mutableStateOf("") }
    fun matches(text: String) = search.isBlank() || text.contains(search.trim(), ignoreCase = true)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(kind.buttonTitle) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = search,
                    onValueChange = { search = it },
                    placeholder = { Text("Suchen") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                LazyColumn(Modifier.heightIn(max = 400.dp)) {
                    when (kind) {
                        TagPick.ITEM -> {
                            val items = state.items
                                .filter { it.id !in excludedItemIds && matches(it.getDisplayTitle()) }
                                .sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.getDisplayTitle() })
                            items(items, key = { it.id }) { item ->
                                ItemHeader(
                                    item,
                                    item.containerId?.let { state.container(it)?.name },
                                    Modifier
                                        .fillMaxWidth()
                                        .clickable { onPick(ScannedTag(item.rfidTag, item.itemData)) }
                                        .padding(vertical = 8.dp)
                                )
                            }
                        }
                        TagPick.CONTAINER -> {
                            items(state.containers.filter { matches(it.name) }, key = { it.id }) { container ->
                                Row(
                                    Modifier
                                        .fillMaxWidth()
                                        .clickable { onPick(ScannedTag(container.rfidTag)) }
                                        .padding(vertical = 12.dp)
                                ) {
                                    Text(container.name, Modifier.weight(1f))
                                    Text("${state.filamentCount(container.id)}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = { TextButton(onClick = onDismiss) { Text("Abbrechen") } }
    )
}

/** NFC-Leser für einen Bildschirm; der Scan endet, wenn der Bildschirm verlassen wird. */
@Composable
fun rememberNfcReader(): NFCReader {
    val context = LocalContext.current
    val reader = remember { NFCReader(context as Activity) }
    DisposableEffect(reader) { onDispose { reader.stopScanning() } }
    return reader
}

/** Eingabefeld für eine Dezimalzahl (Komma oder Punkt). */
@Composable
fun DecimalField(label: String, value: String, placeholder: String = "z. B. 1000", onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = { text -> if (text.all { it.isDigit() || it == ',' || it == '.' }) onChange(text) },
        label = { Text(label) },
        placeholder = { Text(placeholder) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
        modifier = Modifier.fillMaxWidth()
    )
}

/** Eingabefeld für die Lichtdurchlässigkeit (TD in mm). */
@Composable
fun TransmissionField(value: String, onChange: (String) -> Unit) =
    DecimalField("Lichtdurchlässigkeit (TD, mm)", value, placeholder = "z. B. 3,5", onChange = onChange)

/** Auswahl eines Spulenplatzes: Lager, ein Container im Lager oder ein Kopf eines Druckers. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SpoolPlacePicker(label: String, state: StoreState, selection: SpoolPlace, onSelect: (SpoolPlace) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    val options = remember(state.containers, state.printers) {
        listOf(SpoolPlace.STORAGE to "Lager") +
            state.storageContainers().map { SpoolPlace.container(it.id) to it.name } +
            state.printers.flatMap { printer ->
                (0 until printer.toolCount).map { tool -> SpoolPlace.printer(printer.id, tool) to printer.placeName(tool) }
            }
    }
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
        OutlinedTextField(
            value = selection.text(state.printers, state.containers),
            onValueChange = {},
            readOnly = true,
            label = { Text(label) },
            trailingIcon = { Icon(Icons.Default.ArrowDropDown, contentDescription = null) },
            modifier = Modifier
                .fillMaxWidth()
                .menuAnchor()
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            options.forEach { (option, name) ->
                DropdownMenuItem(text = { Text(name) }, onClick = {
                    onSelect(option)
                    expanded = false
                })
            }
        }
    }
}

/** Abschnitt mit Überschrift als Karte. */
@Composable
fun SectionCard(title: String? = null, footer: String? = null, content: @Composable ColumnScope.() -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        title?.let { Text(it, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary) }
        Card(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp), content = content)
        }
        footer?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
    }
}
