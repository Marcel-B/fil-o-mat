package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
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
import com.filomat.app.data.sync.BackendClient
import com.filomat.app.data.sync.BackendUpdateStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.text.DateFormat
import java.util.Date
import com.filomat.app.i18n.tr

/** Verbindung zum Fil-o-mat-Backend, über das alle Geräte dieselben Container und Filamente sehen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BackendScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val sync by store.sync.collectAsState()
    val scope = rememberCoroutineScope()

    var serverUrl by remember { mutableStateOf(store.serverUrl) }
    var apiKey by remember { mutableStateOf(store.apiKey) }
    var resultMessage by remember { mutableStateOf<String?>(null) }
    var confirmingUpdate by remember { mutableStateOf(false) }
    var isChecking by remember { mutableStateOf(false) }
    var updateError by remember { mutableStateOf<String?>(null) }

    val isValid = BackendClient.create(serverUrl, apiKey) != null

    LaunchedEffect(Unit) { store.refreshUpdateStatus() }
    // Während der Aktualisierung den Stand verfolgen, bis das Backend wieder läuft
    LaunchedEffect(sync.updateStatus?.isUpdating) {
        while (store.sync.value.updateStatus?.isUpdating == true) {
            delay(3000)
            store.refreshUpdateStatus()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(tr("Backend")) },
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
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(tr("Server"), style = MaterialTheme.typography.titleMedium)
                    OutlinedTextField(
                        value = serverUrl,
                        onValueChange = { serverUrl = it },
                        label = { Text(tr("Server-Adresse")) },
                        placeholder = { Text("http://nas.local:8080") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = apiKey,
                        onValueChange = { apiKey = it },
                        label = { Text(tr("API-Schlüssel (optional)")) },
                        singleLine = true,
                        visualTransformation = PasswordVisualTransformation(),
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        tr("Beim Verbinden werden die Daten dieses Geräts hochgeladen (nichts wird gelöscht) und danach der gemeinsame Stand übernommen, z. B. Container aus der iOS-App. Danach wird jede Änderung sofort übertragen."),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Button(
                        enabled = isValid && !sync.isSyncing,
                        onClick = {
                            resultMessage = null
                            scope.launch {
                                val result = store.connect(serverUrl, apiKey) ?: return@launch
                                val now = store.current()
                                resultMessage = buildString {
                                    append(tr("Hochgeladen: %s Container, %s Filamente. ", result.containers, result.items))
                                    append(tr("Jetzt %s Container und %s Filamente.", now.containers.size, now.items.size))
                                    result.skipped.forEach { append("\n• ").append(it) }
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        if (sync.isSyncing) {
                            CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.width(8.dp))
                        }
                        Text(if (sync.connected) tr("Erneut abgleichen") else tr("Verbinden und abgleichen"))
                    }
                    if (sync.connected) {
                        OutlinedButton(
                            onClick = {
                                store.disconnect()
                                serverUrl = ""
                                apiKey = ""
                                resultMessage = null
                            },
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                            modifier = Modifier.fillMaxWidth()
                        ) { Text(tr("Verbindung trennen")) }
                    }
                }
            }

            resultMessage?.let { InfoCard(tr("Ergebnis"), it) }
            sync.error?.let { InfoCard(tr("Fehler"), it, isError = true) }
            sync.compatibility?.message?.let { InfoCard(tr("Version"), it, isError = true) }
            sync.lastSyncedAt?.takeIf { sync.connected }?.let {
                InfoCard(tr("Letzter Abgleich"), DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT).format(Date(it)))
            }

            sync.updateStatus?.let { update ->
                UpdateCard(
                    update = update,
                    isChecking = isChecking,
                    updateError = updateError,
                    onCheck = {
                        updateError = null
                        isChecking = true
                        scope.launch {
                            val previous = store.sync.value.updateStatus?.checkedAt
                            try {
                                store.checkForUpdate()
                                // Der Updater auf dem Server braucht ein paar Sekunden für git fetch
                                for (attempt in 1..10) {
                                    delay(1000)
                                    store.refreshUpdateStatus()
                                    if (store.sync.value.updateStatus?.checkedAt != previous) break
                                }
                            } catch (e: Exception) {
                                updateError = ContainerStore.describe(e)
                            }
                            isChecking = false
                        }
                    },
                    onInstall = { confirmingUpdate = true }
                )
            }
        }
    }

    if (confirmingUpdate) {
        AlertDialog(
            onDismissRequest = { confirmingUpdate = false },
            title = { Text(tr("Backend aktualisieren?")) },
            text = { Text(tr("Der Server holt die neue Version, baut sie und startet das Backend neu. Das dauert ein paar Minuten; so lange ist kein Abgleich möglich.")) },
            confirmButton = {
                TextButton(onClick = {
                    confirmingUpdate = false
                    updateError = null
                    scope.launch {
                        try {
                            store.installUpdate()
                        } catch (e: Exception) {
                            updateError = ContainerStore.describe(e)
                        }
                    }
                }) { Text(tr("Jetzt aktualisieren")) }
            },
            dismissButton = { TextButton(onClick = { confirmingUpdate = false }) { Text(tr("Abbrechen")) } }
        )
    }
}

@Composable
private fun InfoCard(title: String, text: String, isError: Boolean = false) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall)
            Text(text, color = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface)
        }
    }
}

@Composable
private fun UpdateCard(
    update: BackendUpdateStatus,
    isChecking: Boolean,
    updateError: String?,
    onCheck: () -> Unit,
    onInstall: () -> Unit
) {
    val dateFormat = remember { DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT) }
    fun describe(sha: String, date: Long?): String {
        val short = sha.take(7)
        return if (date == null) short else tr("%s vom %s", short, dateFormat.format(Date(date)))
    }
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                if (update.updateAvailable && !update.isUpdating) tr("Neue Version (%s Commits)", update.commits.size) else tr("Version"),
                style = MaterialTheme.typography.titleMedium
            )
            update.currentVersion?.let {
                Text(tr("Läuft: %s", describe(it, update.currentDate)), style = MaterialTheme.typography.bodyMedium)
            }
            when {
                update.isUpdating -> Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(tr("Wird aktualisiert …"), modifier = Modifier.weight(1f))
                    CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
                }
                update.updateAvailable -> {
                    update.commits.forEach { commit ->
                        Column {
                            Text(commit.subject, style = MaterialTheme.typography.bodyMedium)
                            Text(
                                describe(commit.sha, commit.date),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                    Button(onClick = onInstall, modifier = Modifier.fillMaxWidth()) { Text(tr("Aktualisieren …")) }
                }
                else -> Text(
                    tr("Das Backend ist auf dem neuesten Stand."),
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            update.message?.let { message ->
                if (update.failed) Text(message, color = MaterialTheme.colorScheme.error)
                else if (!update.isUpdating) Text(message, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            updateError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            if (!update.isUpdating) {
                OutlinedButton(onClick = onCheck, enabled = !isChecking, modifier = Modifier.fillMaxWidth()) {
                    if (isChecking) {
                        CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
                        Spacer(Modifier.width(8.dp))
                    }
                    Text(tr("Nach neuer Version suchen"))
                }
            }
            update.checkedAt?.let {
                Text(
                    tr("Zuletzt geprüft %s, Branch %s.", dateFormat.format(Date(it)), update.branch ?: "?"),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}
