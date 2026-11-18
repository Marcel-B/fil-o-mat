package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.sync.Spool
import com.filomat.app.data.sync.SpoolFilter
import com.filomat.app.data.sync.SpoolListEntry
import com.filomat.app.data.sync.StoreState
import com.filomat.app.data.sync.spoolEntries
import com.filomat.app.i18n.tr
import com.filomat.app.ui.components.ColorDot
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.MainNavigationBar
import com.filomat.app.ui.components.MainTab

/**
 * Alle angebrochenen Spulen, getrennt von den Sorten unter "Filamente" (wie die Seite "Spulen" der Web-Oberfläche
 * und der Tab in der iOS-App). Antippen öffnet die Spule: wiegen, umstecken, als leer markieren, weiter zur Sorte.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SpoolsScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val sync by store.sync.collectAsState()
    var query by remember { mutableStateOf("") }
    var filter by remember { mutableStateOf(SpoolFilter.ALL) }
    var spoolToEdit by remember { mutableStateOf<Spool?>(null) }
    val entries = remember(state, query, filter) { state.spoolEntries(query, filter) }

    Scaffold(
        topBar = { TopAppBar(title = { Text(tr("Spulen")) }) },
        bottomBar = { MainNavigationBar(navController, MainTab.SPOOLS) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 16.dp),
                placeholder = { Text(tr("Suchen...")) },
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = tr("Suchen")) },
                singleLine = true
            )
            Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SpoolFilter.entries.forEach { option ->
                    FilterChip(selected = filter == option, onClick = { filter = option }, label = { Text(option.title) })
                }
            }

            if (entries.isEmpty()) {
                Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                    Text(
                        text = when {
                            !sync.connected && state.spools.isEmpty() -> tr("Kein Backend verbunden. Angebrochene Spulen führt das Backend.")
                            query.isNotBlank() -> tr("Keine Ergebnisse gefunden")
                            else -> tr("Keine Spulen. Spulen brichst du beim Einlagern oder unter „Filamente“ an.")
                        },
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(entries, key = { it.spool.id }) { entry ->
                        SpoolCard(entry, onClick = { spoolToEdit = entry.spool })
                    }
                    item {
                        Text(
                            if (state.spools.size == 1) tr("1 Spule") else tr("%s Spulen", state.spools.size),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(vertical = 8.dp)
                        )
                    }
                }
            }
        }
    }

    spoolToEdit?.let { spool ->
        SpoolEditDialog(
            spool = spool,
            emptySpoolWeight = state.item(spool.itemId)?.spoolWeight,
            state = state,
            store = store,
            onDismiss = { spoolToEdit = null },
            onOpenItem = { item ->
                spoolToEdit = null
                navController.navigate("item/${item.id}")
            }
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SpoolCard(entry: SpoolListEntry, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().padding(12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ColorDot(entry.item?.itemData?.color_hex, 32.dp)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        "${entry.title} #${entry.spool.number}",
                        style = MaterialTheme.typography.titleSmall,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f)
                    )
                    // Grau: nicht gewogen, das Backend rechnet mit einer vollen Spule
                    Text(
                        entry.remaining?.let(GramText::label) ?: tr("Rest unbekannt"),
                        style = MaterialTheme.typography.bodyMedium,
                        color = if (entry.isWeighed) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                Text(
                    listOfNotNull(entry.item?.itemData?.color?.takeIf { it.isNotBlank() }, entry.place).joinToString(" · "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                entry.fraction?.let { fraction ->
                    LinearProgressIndicator(
                        progress = fraction.toFloat(),
                        modifier = Modifier.fillMaxWidth(),
                        color = when {
                            fraction < 0.1 -> MaterialTheme.colorScheme.error
                            fraction < 0.25 -> androidx.compose.ui.graphics.Color(0xFFFB8C00)
                            else -> MaterialTheme.colorScheme.primary
                        }
                    )
                }
            }
        }
    }
}
