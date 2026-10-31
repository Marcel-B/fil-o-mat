package com.filomat.app.ui.screens

import android.graphics.BitmapFactory
import android.text.format.DateUtils
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.sync.NotificationSettings
import com.filomat.app.data.sync.NotificationSettingsRequest
import com.filomat.app.data.sync.PrinterLive
import com.filomat.app.data.sync.PrinterStatus
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolPlace
import com.filomat.app.data.sync.StatsResponse
import com.filomat.app.data.sync.StatsTotals
import com.filomat.app.data.sync.ToolLive
import com.filomat.app.data.sync.gramsText
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.SectionCard
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.filomat.app.i18n.tr

/** Abstand, in dem offene Ansichten den Zustand der Drucker neu holen. */
const val PRINTER_LIVE_INTERVAL_MS = 15_000L

@Composable
private fun stateColor(status: PrinterStatus): Color = when (status.state) {
    "Printing" -> MaterialTheme.colorScheme.primary
    "Paused" -> Color(0xFFE65100)
    "Error" -> MaterialTheme.colorScheme.error
    "Finished", "Cancelled" -> MaterialTheme.colorScheme.onSurface
    else -> MaterialTheme.colorScheme.onSurfaceVariant
}

private val WarningColor = Color(0xFFE65100)

/** Eine Zeile Zustand: "Druckt benchy · 45 % · noch 32 min", Fortschritt und Warnungen. */
@Composable
fun PrinterStatusLine(live: PrinterLive) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(live.status.summary, style = MaterialTheme.typography.bodySmall, color = stateColor(live.status))
        val progress = live.status.progress
        if (live.status.isActive && progress != null) {
            LinearProgressIndicator(
                progress = (progress / 100).toFloat().coerceIn(0f, 1f),
                modifier = Modifier.fillMaxWidth()
            )
        }
        val warnings = live.warnings
        if (warnings.isNotEmpty()) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                Icon(Icons.Default.Warning, contentDescription = null, tint = WarningColor, modifier = Modifier.size(16.dp))
                Text(
                    if (warnings.size == 1) warnings[0].text else tr("%s Warnungen", warnings.size),
                    style = MaterialTheme.typography.bodySmall,
                    color = WarningColor,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
            }
        }
    }
}

/** Zustand eines Druckers im Detail: Druck, Vorschau, Hinweise, Spulen pro Kopf, Temperaturen, Feuchte. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrinterDetailScreen(printerId: String, navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val liveMap by store.printerLive.collectAsState()
    val printers by store.printers.collectAsState(initial = emptyList())
    val printer = printers.firstOrNull { it.id == printerId }
    val live = liveMap[printerId]
    val spools by store.spools.collectAsState(initial = emptyList())
    val items by store.allItems.collectAsState(initial = emptyList())
    val scope = rememberCoroutineScope()
    var fixing by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf(false) }
    var thumbnail by remember { mutableStateOf<ImageBitmap?>(null) }

    LaunchedEffect(Unit) {
        while (true) {
            store.refreshPrinterLive()
            delay(PRINTER_LIVE_INTERVAL_MS)
        }
    }
    LaunchedEffect(live?.status?.jobKey, live?.status?.hasThumbnail) {
        if (live?.status?.hasThumbnail != true) {
            thumbnail = null
            return@LaunchedEffect
        }
        val bytes = runCatching { store.backend?.printerThumbnail(printerId) }.getOrNull() ?: return@LaunchedEffect
        thumbnail = withContext(Dispatchers.Default) { BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap() }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(printer?.name ?: live?.name ?: tr("Drucker")) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = tr("Zurück"))
                    }
                },
                actions = {
                    if (printer != null) {
                        IconButton(onClick = { editing = true }) {
                            Icon(Icons.Default.Edit, contentDescription = tr("Bearbeiten"))
                        }
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
            if (live == null) {
                Text(
                    tr("Das Backend hat den Drucker noch nicht abgefragt oder kennt keinen Zustand (ältere Version?)."),
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                return@Column
            }
            val status = live.status
            SectionCard(footer = live.updatedDate?.let { tr("Abgefragt %s", DateUtils.getRelativeTimeSpanString(it.time)) }) {
                Text(status.title, style = MaterialTheme.typography.titleMedium, color = stateColor(status))
                if (status.hasJob && !status.jobName.isNullOrEmpty()) LabeledRow(tr("Druck"), status.jobName)
                val progress = status.progress
                if (status.isActive && progress != null) {
                    LinearProgressIndicator(progress = (progress / 100).toFloat().coerceIn(0f, 1f), modifier = Modifier.fillMaxWidth())
                    Row {
                        Text("${progress.toInt()} %", style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f))
                        status.remainingText?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                    }
                }
                if (status.hasJob && status.layer != null && (status.totalLayers ?: 0) > 0) {
                    LabeledRow(tr("Schicht"), tr("%s von %s", status.layer, status.totalLayers))
                }
                status.elapsedSeconds?.takeIf { it > 0 && status.hasJob }?.let { LabeledRow(tr("Druckzeit"), PrinterStatus.duration(it)) }
                status.message?.takeIf { it.isNotEmpty() }?.let {
                    Text(
                        it,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (status.state == "Error" || status.state == "Offline") MaterialTheme.colorScheme.error
                        else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                val image = thumbnail
                if (image != null && status.hasJob) {
                    Image(
                        bitmap = image,
                        contentDescription = tr("Vorschau"),
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(max = 200.dp)
                    )
                }
            }
            if (live.hints.isNotEmpty()) {
                SectionCard(tr("Hinweise")) {
                    live.hints.forEach { hint ->
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Icon(
                                Icons.Default.Warning,
                                contentDescription = null,
                                tint = if (hint.warning) WarningColor else MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.size(18.dp)
                            )
                            Text(hint.text, style = MaterialTheme.typography.bodyMedium)
                        }
                        // Der Drucker erkennt das Plättchen einer anderen Sorte: ein Tipp legt eine ihrer Spulen in den Kopf
                        val tool = live.tools.firstOrNull { it.tool == hint.tool }
                        val tag = tool?.reported?.tagUid
                        val detected = if (hint.kind == "TagMismatch" && tag != null)
                            items.firstOrNull { item -> item.allTags.any { it.equals(tag, ignoreCase = true) } }?.takeIf { it.id != tool?.itemId }
                        else null
                        if (tool != null && detected != null) {
                            val candidates = spools
                                .filter { it.itemId == detected.id && (it.printerId != printerId || (it.tool ?: 0) != tool.tool) }
                                .sortedWith(compareBy({ if (it.printerId == null) 0 else 1 }, { it.number }))
                            DetectedFix(
                                itemName = detected.name,
                                candidates = candidates,
                                canOpen = (detected.spoolsInStock ?: 0) > 0,
                                enabled = !fixing,
                                printerName = { id -> printers.firstOrNull { it.id == id }?.name },
                                onChoose = { spool ->
                                    scope.launch {
                                        fixing = true
                                        runCatching { store.placeSpool(spool, SpoolPlace.printer(printerId, tool.tool)) }
                                        store.refreshPrinterLive()
                                        fixing = false
                                    }
                                },
                                onOpen = {
                                    scope.launch {
                                        fixing = true
                                        runCatching { store.openSpool(detected.id, SpoolPlace.printer(printerId, tool.tool)) }
                                        store.refreshPrinterLive()
                                        fixing = false
                                    }
                                }
                            )
                        }
                    }
                }
            }
            if (live.tools.isNotEmpty()) {
                val title = when {
                    live.tools.size <= 1 -> tr("Spule")
                    live.type == "Moonraker" || live.type == "SnapmakerU1" -> tr("Köpfe")
                    else -> tr("Slots")
                }
                SectionCard(title) {
                    live.tools.forEach { tool ->
                        ToolLiveRow(tool, isActive = status.isActive && status.activeTool == tool.tool && live.tools.size > 1)
                        // Plättchen erkannt, aber welche lose Spule der Sorte steckt dort?
                        val loose = if (tool.spoolId == null && tool.itemId != null)
                            spools.filter { it.itemId == tool.itemId && it.printerId == null }.sortedBy { it.number }
                        else emptyList()
                        if (loose.isNotEmpty()) {
                            SpoolChoice(loose) { spool ->
                                scope.launch {
                                    runCatching { store.placeSpool(spool, SpoolPlace.printer(printerId, tool.tool)) }
                                    store.refreshPrinterLive()
                                }
                            }
                        }
                    }
                }
            }
            if (status.heaters.isNotEmpty()) {
                SectionCard(tr("Temperaturen")) {
                    status.heaters.forEach { LabeledRow(it.name, it.text) }
                }
            }
            if (status.dryers.isNotEmpty()) {
                SectionCard(tr("Trockenbox")) {
                    status.dryers.forEach { LabeledRow(it.name, it.text) }
                }
            }
        }
    }

    if (editing && printer != null) {
        PrinterDialog(store = store, printer = printer, onDismiss = { editing = false })
    }
}

@Composable
private fun LabeledRow(label: String, value: String) {
    Row(modifier = Modifier.fillMaxWidth()) {
        Text(label, modifier = Modifier.weight(1f))
        Text(value, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** Auswahl der losen Spule, die in einem Kopf steckt. */
@Composable
private fun SpoolChoice(loose: List<Spool>, onChoose: (Spool) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        TextButton(onClick = { expanded = true }) { Text(tr("Welche Spule steckt hier?")) }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            loose.forEach { spool ->
                DropdownMenuItem(
                    text = { Text(spool.remainingWeight?.let { "#${spool.number} · ${gramsText(it)}" } ?: "#${spool.number}") },
                    onClick = {
                        expanded = false
                        onChoose(spool)
                    }
                )
            }
        }
    }
}

/**
 * Knopf am Hinweis „Der Drucker erkennt …“: legt eine Spule der erkannten Sorte in den Kopf, die bisherige wandert ins
 * Lager. Eine Spule: ein Tipp. Mehrere: Auswahl. Keine angebrochene, aber Vorrat: neue Spule anbrechen.
 */
@Composable
private fun DetectedFix(
    itemName: String,
    candidates: List<Spool>,
    canOpen: Boolean,
    enabled: Boolean,
    printerName: (String) -> String?,
    onChoose: (Spool) -> Unit,
    onOpen: () -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    when {
        candidates.size == 1 -> TextButton(onClick = { onChoose(candidates[0]) }, enabled = enabled) {
            Text(tr("%s #%s übernehmen", itemName, candidates[0].number))
        }
        candidates.size > 1 -> Box {
            TextButton(onClick = { expanded = true }, enabled = enabled) { Text(tr("%s übernehmen", itemName)) }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                candidates.forEach { spool ->
                    val parts = listOfNotNull(
                        "#${spool.number}",
                        spool.remainingWeight?.let { gramsText(it) },
                        spool.printerId?.let(printerName)
                    )
                    DropdownMenuItem(
                        text = { Text(parts.joinToString(" · ")) },
                        onClick = {
                            expanded = false
                            onChoose(spool)
                        }
                    )
                }
            }
        }
        canOpen -> TextButton(onClick = onOpen, enabled = enabled) { Text(tr("Neue Spule %s anbrechen", itemName)) }
    }
}

/** Ein Kopf bzw. Slot: Spule laut App, Restgewicht und was der Druck noch braucht. */
@Composable
private fun ToolLiveRow(tool: ToolLive, isActive: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        ColorDot(tool.colorHex, size = 28.dp)
        Column(modifier = Modifier.weight(1f)) {
            if (tool.place.isNotEmpty()) {
                Text(
                    tool.place + if (isActive) tr(" ▸ fördert") else "",
                    style = MaterialTheme.typography.labelSmall,
                    color = if (isActive) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Text(tool.title, color = if (tool.itemName == null) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface)
            tool.reported?.remainPercent?.let {
                Text(tr("Drucker meldet %s %% Rest", it.toInt()), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Column(horizontalAlignment = Alignment.End) {
            tool.remainingGrams?.let { Text(gramsText(it)) }
            tool.neededGrams?.let {
                Text(
                    tr("braucht %s", gramsText(it)),
                    style = MaterialTheme.typography.bodySmall,
                    color = if (tool.enough == false) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

/** Verbrauch, Drucke und Druckzeit pro Monat, Drucker und Sorte. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StatsScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    var months by remember { mutableIntStateOf(12) }
    var stats by remember { mutableStateOf<StatsResponse?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(months) {
        val client = store.backend
        if (client == null) {
            errorMessage = tr("Kein Backend verbunden.")
            return@LaunchedEffect
        }
        try {
            stats = client.stats(months)
            errorMessage = null
        } catch (e: Exception) {
            errorMessage = ContainerStore.describe(e)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(tr("Statistik")) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = tr("Zurück"))
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
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(3 to tr("3 Monate"), 12 to tr("12 Monate"), 24 to tr("2 Jahre")).forEach { (value, label) ->
                    FilterChip(selected = months == value, onClick = { months = value }, label = { Text(label) })
                }
            }
            errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            val current = stats
            if (current == null) {
                if (errorMessage == null) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                return@Column
            }
            SectionCard(tr("Gesamt")) {
                LabeledRow(tr("Verbrauch"), gramsText(current.totals.grams))
                LabeledRow(
                    tr("Drucke"),
                    if (current.totals.cancelled > 0) tr("%s (%s abgebrochen)", current.totals.jobs, current.totals.cancelled) else "${current.totals.jobs}"
                )
                if (current.totals.printSeconds > 0) LabeledRow(tr("Druckzeit"), PrinterStatus.duration(current.totals.printSeconds))
                if (current.totals.cancelledGrams > 0) LabeledRow(tr("Davon abgebrochen"), gramsText(current.totals.cancelledGrams))
            }
            if (current.totals.grams > 0) {
                SectionCard(tr("Verbrauch pro Monat")) {
                    MonthBars(current, withYear = months > 12)
                }
            }
            if (current.printers.isNotEmpty()) {
                SectionCard(tr("Drucker")) {
                    current.printers.forEach { printer ->
                        Column {
                            LabeledRow(printer.name, gramsText(printer.totals.grams))
                            Text(detail(printer.totals), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
            if (current.items.isNotEmpty()) {
                SectionCard(tr("Sorten")) {
                    current.items.take(15).forEach { item ->
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            ColorDot(item.colorHex, size = 24.dp, showsPlaceholder = false)
                            Column(modifier = Modifier.weight(1f)) {
                                Text(item.name)
                                Text(
                                    if (item.jobs == 1) tr("1 Druck") else tr("%s Drucke", item.jobs),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            Text(gramsText(item.grams))
                        }
                    }
                }
            }
            if (current.types.size > 1) {
                SectionCard(tr("Material")) {
                    current.types.forEach { LabeledRow(it.type, gramsText(it.grams)) }
                }
            }
        }
    }
}

private fun detail(totals: StatsTotals): String {
    val parts = mutableListOf(if (totals.jobs == 1) tr("1 Druck") else tr("%s Drucke", totals.jobs))
    if (totals.cancelled > 0) parts += tr("%s abgebrochen", totals.cancelled)
    if (totals.printSeconds > 0) parts += PrinterStatus.duration(totals.printSeconds)
    return parts.joinToString(" · ")
}

/** Einfaches Balkendiagramm, ohne zusätzliche Bibliothek. */
@Composable
private fun MonthBars(stats: StatsResponse, withYear: Boolean) {
    val max = stats.months.maxOfOrNull { it.grams }?.takeIf { it > 0 } ?: return
    Text(tr("max. %s", gramsText(max)), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(140.dp),
        horizontalArrangement = Arrangement.spacedBy(2.dp),
        verticalAlignment = Alignment.Bottom
    ) {
        stats.months.forEach { month ->
            Box(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight((month.grams / max).toFloat().coerceIn(0.01f, 1f))
                    .background(MaterialTheme.colorScheme.primary, RoundedCornerShape(topStart = 3.dp, topEnd = 3.dp))
            )
        }
    }
    // Beschriftung nur für jeden dritten Monat, damit sie lesbar bleibt
    val step = if (stats.months.size > 12) 6 else if (stats.months.size > 6) 3 else 1
    Row(modifier = Modifier.fillMaxWidth()) {
        stats.months.forEachIndexed { index, month ->
            Text(
                if (index % step == 0) month.label(withYear) else "",
                style = MaterialTheme.typography.labelSmall,
                maxLines = 1,
                softWrap = false,
                modifier = Modifier.weight(1f)
            )
        }
    }
}

/** ntfy-Adresse für Meldungen bei Druckende, Fehlern und Spulen, die nicht reichen. */
@Composable
fun NotificationDialog(store: ContainerStore, onDismiss: () -> Unit) {
    val scope = rememberCoroutineScope()
    var settings by remember { mutableStateOf<NotificationSettings?>(null) }
    var url by remember { mutableStateOf("") }
    var token by remember { mutableStateOf("") }
    var isWorking by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(Unit) {
        val client = store.backend ?: return@LaunchedEffect
        try {
            val loaded = client.notificationSettings()
            settings = loaded
            url = if (loaded.fromApp) loaded.url.orEmpty() else ""
        } catch (e: Exception) {
            errorMessage = ContainerStore.describe(e)
        }
    }

    AlertDialog(
        onDismissRequest = { if (!isWorking) onDismiss() },
        title = { Text(tr("Meldungen")) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = url,
                    onValueChange = { url = it },
                    label = { Text(tr("ntfy-Thema")) },
                    placeholder = { Text("https://ntfy.sh/filomat-…") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = token,
                    onValueChange = { token = it },
                    label = { Text(if (settings?.hasToken == true) tr("Token (leer = unverändert)") else tr("Token (optional)")) },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth()
                )
                Text(
                    tr("Das Backend meldet fertige und abgebrochene Drucke mit Restgewicht, Fehler und Spulen, die für den laufenden Druck nicht reichen. Ein schwer erratbares Thema auf ntfy.sh reicht; in der ntfy-App abonnieren (klappt auch ohne Google-Dienste)."),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                if (settings?.configured == true && settings?.fromApp == false) {
                    Text(tr("Zurzeit gilt die Adresse aus der Server-Konfiguration. Eine Adresse hier geht vor."), style = MaterialTheme.typography.bodySmall)
                }
                TextButton(enabled = !isWorking && settings?.configured == true, onClick = {
                    val client = store.backend ?: return@TextButton
                    isWorking = true
                    scope.launch {
                        try {
                            client.sendTestNotification()
                            errorMessage = null
                            message = tr("Testmeldung verschickt.")
                        } catch (e: Exception) {
                            errorMessage = ContainerStore.describe(e)
                        }
                        isWorking = false
                    }
                }) { Text(tr("Testmeldung schicken")) }
                if (isWorking) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                message?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(enabled = !isWorking, onClick = {
                val client = store.backend ?: return@Button
                isWorking = true
                scope.launch {
                    try {
                        settings = client.saveNotificationSettings(
                            NotificationSettingsRequest(url.trim(), token.trim().ifEmpty { null })
                        )
                        token = ""
                        errorMessage = null
                        message = if (url.isBlank()) tr("Adresse entfernt.") else tr("Gesichert.")
                    } catch (e: Exception) {
                        errorMessage = ContainerStore.describe(e)
                    }
                    isWorking = false
                }
            }) { Text(tr("Sichern")) }
        },
        dismissButton = { TextButton(enabled = !isWorking, onClick = onDismiss) { Text(tr("Schließen")) } }
    )
}
