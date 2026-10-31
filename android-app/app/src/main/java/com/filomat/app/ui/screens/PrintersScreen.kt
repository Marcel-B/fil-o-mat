package com.filomat.app.ui.screens

import android.text.format.DateUtils
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.BarChart
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.sync.Printer
import com.filomat.app.data.sync.PrinterLive
import com.filomat.app.data.sync.PrinterRequest
import com.filomat.app.data.sync.PrinterUpdateRequest
import com.filomat.app.data.sync.PrinterType
import com.filomat.app.ui.components.MainNavigationBar
import com.filomat.app.ui.components.MainTab
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import com.filomat.app.i18n.tr

/** Drucker im Backend: anlegen, bearbeiten, abgleichen, entfernen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrintersScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val sync by store.sync.collectAsState()
    val printers by store.printers.collectAsState(initial = emptyList())
    val scope = rememberCoroutineScope()
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var showingAdd by remember { mutableStateOf(false) }
    var printerToDelete by remember { mutableStateOf<Printer?>(null) }
    var printerToEdit by remember { mutableStateOf<Printer?>(null) }
    var syncingId by remember { mutableStateOf<String?>(null) }
    var isLoading by remember { mutableStateOf(false) }
    var showingNotifications by remember { mutableStateOf(false) }
    val liveMap by store.printerLive.collectAsState()

    fun load() {
        scope.launch {
            isLoading = true
            errorMessage = try {
                store.reloadPrinters()
                null
            } catch (e: Exception) {
                ContainerStore.describe(e)
            }
            isLoading = false
        }
    }

    LaunchedEffect(sync.connected) { if (sync.connected) load() }

    // Solange die Liste offen ist, den Zustand der Drucker alle 15 Sekunden holen
    LaunchedEffect(sync.connected) {
        while (sync.connected) {
            store.refreshPrinterLive()
            delay(PRINTER_LIVE_INTERVAL_MS)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(tr("Drucker")) },
                actions = {
                    if (sync.connected) {
                        IconButton(onClick = { navController.navigate("stats") }) {
                            Icon(Icons.Default.BarChart, contentDescription = tr("Statistik"))
                        }
                        IconButton(onClick = { showingNotifications = true }) {
                            Icon(Icons.Default.Notifications, contentDescription = tr("Meldungen"))
                        }
                        IconButton(onClick = { load() }, enabled = !isLoading) {
                            Icon(Icons.Default.Sync, contentDescription = tr("Neu laden"))
                        }
                    }
                }
            )
        },
        bottomBar = { MainNavigationBar(navController, MainTab.PRINTERS) },
        floatingActionButton = {
            if (sync.connected) {
                FloatingActionButton(onClick = { showingAdd = true }) {
                    Icon(Icons.Default.Add, contentDescription = tr("Drucker hinzufügen"))
                }
            }
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            if (!sync.connected) {
                item {
                    EmptyHint(
                        tr("Kein Backend verbunden"),
                        tr("Drucker werden im Backend verwaltet. Verbinde es über das Wolken-Symbol in der Container-Liste.")
                    )
                }
            }
            if (isLoading) item { LinearProgressIndicator(modifier = Modifier.fillMaxWidth()) }
            errorMessage?.let { item { Text(it, color = MaterialTheme.colorScheme.error) } }
            if (sync.connected && printers.isEmpty() && errorMessage == null && !isLoading) {
                item {
                    EmptyHint(tr("Keine Drucker"), tr("Füge einen Drucker hinzu, damit sein Verbrauch automatisch abgezogen wird."))
                }
            }
            items(printers, key = { it.id }) { printer ->
                PrinterCard(
                    printer = printer,
                    live = liveMap[printer.id],
                    isSyncing = syncingId == printer.id,
                    onOpen = { navController.navigate("printer/${printer.id}") },
                    onSync = {
                        val client = store.backend ?: return@PrinterCard
                        syncingId = printer.id
                        scope.launch {
                            try {
                                val result = client.syncPrinter(printer.id)
                                errorMessage = result.error
                                store.refresh()
                            } catch (e: Exception) {
                                errorMessage = ContainerStore.describe(e)
                            }
                            syncingId = null
                        }
                    },
                    onEdit = { printerToEdit = printer },
                    onDelete = { printerToDelete = printer }
                )
            }
        }
    }

    if (showingNotifications) {
        NotificationDialog(store = store, onDismiss = { showingNotifications = false })
    }

    if (showingAdd) {
        PrinterDialog(store = store, printer = null, onDismiss = { showingAdd = false })
    }

    printerToEdit?.let { printer ->
        PrinterDialog(store = store, printer = printer, onDismiss = {
            printerToEdit = null
            load()
        })
    }

    printerToDelete?.let { printer ->
        AlertDialog(
            onDismissRequest = { printerToDelete = null },
            title = { Text(tr("Drucker entfernen")) },
            text = { Text(tr("'%s' und sein Container werden entfernt. Eingelegte Spulen wandern nach „Lose“, ihr Verbrauch bleibt erhalten.", printer.name)) },
            confirmButton = {
                TextButton(onClick = {
                    printerToDelete = null
                    scope.launch {
                        try {
                            store.backend?.deletePrinter(printer.id)
                            store.refresh()
                        } catch (e: Exception) {
                            errorMessage = ContainerStore.describe(e)
                        }
                    }
                }) { Text(tr("Entfernen"), color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { printerToDelete = null }) { Text(tr("Abbrechen")) } }
        )
    }
}

@Composable
private fun EmptyHint(title: String, text: String) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Text(title, style = MaterialTheme.typography.titleMedium)
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PrinterCard(
    printer: Printer,
    live: PrinterLive?,
    isSyncing: Boolean,
    onOpen: () -> Unit,
    onSync: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
    Card(onClick = onOpen, modifier = Modifier.fillMaxWidth()) {
        Row(modifier = Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(printer.name, style = MaterialTheme.typography.titleMedium)
                Text(
                    (if (printer.enabled) "" else tr("Pausiert · ")) +
                    if (printer.toolCount > 1) tr("%s · %s Köpfe · %s", printer.typeTitle, printer.toolCount, printer.baseUrl)
                    else "${printer.typeTitle} · ${printer.baseUrl}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                val error = printer.lastSyncError
                val lastSync = printer.lastSyncDate
                if (live != null && printer.enabled && error == null) PrinterStatusLine(live)
                if (error != null) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        Icon(Icons.Default.Warning, contentDescription = null, tint = MaterialTheme.colorScheme.error, modifier = Modifier.size(16.dp))
                        Text(error, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                    }
                } else if (lastSync != null) {
                    Text(
                        tr("Zuletzt abgeglichen %s", DateUtils.getRelativeTimeSpanString(lastSync.time)),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
            if (isSyncing) {
                CircularProgressIndicator(modifier = Modifier.size(24.dp), strokeWidth = 2.dp)
            } else {
                IconButton(onClick = onSync) {
                    Icon(Icons.Default.Sync, contentDescription = tr("Abgleichen"), tint = MaterialTheme.colorScheme.primary)
                }
            }
            IconButton(onClick = onEdit) {
                Icon(Icons.Default.Edit, contentDescription = tr("Bearbeiten"))
            }
            IconButton(onClick = onDelete) {
                Icon(Icons.Default.Delete, contentDescription = tr("Entfernen"), tint = MaterialTheme.colorScheme.error)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
/** Neuer Drucker ([printer] ist `null`) oder Änderungen an einem vorhandenen, z. B. eine neue IP nach DHCP. */
internal fun PrinterDialog(store: ContainerStore, printer: Printer?, onDismiss: () -> Unit) {
    val scope = rememberCoroutineScope()
    var name by remember { mutableStateOf(printer?.name ?: "") }
    var type by remember { mutableStateOf(printer?.printerType ?: PrinterType.SNAPMAKER_U1) }
    var typeExpanded by remember { mutableStateOf(false) }
    var baseUrl by remember { mutableStateOf(printer?.baseUrl ?: "") }
    var apiKey by remember { mutableStateOf("") }
    var removeApiKey by remember { mutableStateOf(false) }
    var enabled by remember { mutableStateOf(printer?.enabled ?: true) }
    var isSaving by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val isValid = name.isNotBlank() && (baseUrl.trim().startsWith("http://") || baseUrl.trim().startsWith("https://")) &&
        (printer != null || !type.requiresKey || apiKey.isNotBlank())

    AlertDialog(
        onDismissRequest = { if (!isSaving) onDismiss() },
        title = { Text(if (printer == null) tr("Drucker hinzufügen") else tr("Drucker bearbeiten")) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text(tr("Name")) },
                    placeholder = { Text(tr("z. B. Snapmaker U1")) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                if (printer != null) {
                    OutlinedTextField(
                        value = printer.typeTitle,
                        onValueChange = {},
                        readOnly = true,
                        enabled = false,
                        label = { Text(tr("Typ")) },
                        modifier = Modifier.fillMaxWidth()
                    )
                } else ExposedDropdownMenuBox(expanded = typeExpanded, onExpandedChange = { typeExpanded = it }) {
                    OutlinedTextField(
                        value = type.title,
                        onValueChange = {},
                        readOnly = true,
                        label = { Text(tr("Typ")) },
                        trailingIcon = { Icon(Icons.Default.ArrowDropDown, contentDescription = null) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .menuAnchor()
                    )
                    ExposedDropdownMenu(expanded = typeExpanded, onDismissRequest = { typeExpanded = false }) {
                        PrinterType.entries.forEach { option ->
                            DropdownMenuItem(text = { Text(option.title) }, onClick = {
                                type = option
                                typeExpanded = false
                            })
                        }
                    }
                }
                OutlinedTextField(
                    value = baseUrl,
                    onValueChange = { baseUrl = it },
                    label = { Text(tr("Adresse")) },
                    placeholder = { Text(type.addressHint) },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = apiKey,
                    onValueChange = { apiKey = it },
                    label = {
                        Text(
                            if (printer?.hasApiKey == true) {
                                if (type.requiresKey) tr("Neuer Zugangscode (leer = unverändert)") else tr("Neuer API-Schlüssel (leer = unverändert)")
                            } else type.keyLabel
                        )
                    },
                    singleLine = true,
                    enabled = !removeApiKey,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth()
                )
                if (printer?.hasApiKey == true && !type.requiresKey) {
                    SwitchRow(tr("API-Schlüssel entfernen"), removeApiKey) { removeApiKey = it }
                }
                if (printer != null) {
                    SwitchRow(tr("Automatisch abgleichen"), enabled) { enabled = it }
                }
                Text(type.setupHint, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (isSaving) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(enabled = isValid && !isSaving, onClick = {
                val client = store.backend ?: return@Button
                isSaving = true
                errorMessage = null
                scope.launch {
                    try {
                        if (printer != null) {
                            client.updatePrinter(
                                printer.id,
                                PrinterUpdateRequest(
                                    name = name.trim(),
                                    baseUrl = baseUrl.trim(),
                                    apiKey = if (removeApiKey) "" else apiKey.trim().ifEmpty { null },
                                    enabled = enabled
                                )
                            )
                        } else {
                            client.createPrinter(
                                PrinterRequest(
                                    name = name.trim(),
                                    type = type.raw,
                                    baseUrl = baseUrl.trim(),
                                    apiKey = apiKey.trim().ifEmpty { null }
                                )
                            )
                        }
                        store.refresh()
                        onDismiss()
                    } catch (e: Exception) {
                        errorMessage = ContainerStore.describe(e)
                    }
                    isSaving = false
                }
            }) { Text(if (printer == null) tr("Hinzufügen") else tr("Sichern")) }
        },
        dismissButton = { TextButton(enabled = !isSaving, onClick = onDismiss) { Text(tr("Abbrechen")) } }
    )
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}
