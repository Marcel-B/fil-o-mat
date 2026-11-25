package com.filomat.app.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Stock
import com.filomat.app.data.StockEntry
import com.filomat.app.data.sync.StoreState
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.ItemHeader
import com.filomat.app.i18n.tr

/** Vorrat aller Sorten: volle Spulen, angebrochene mit Restgewicht und Platz, und was knapp wird. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StockScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val sync by store.sync.collectAsState()
    var query by remember { mutableStateOf("") }

    val all = remember(state) { Stock.entries(state.items, state.spools) }
    val visible = remember(all, query) {
        val q = query.trim().lowercase()
        if (q.isEmpty()) all
        else all.filter { it.item.getDisplayTitle().lowercase().contains(q) || it.item.name.lowercase().contains(q) }
    }
    val low = visible.filter { it.isLow() }
    val groups = visible.groupBy { it.item.itemData?.type?.takeIf { t -> t.isNotBlank() } ?: tr("Ohne Typ") }.toSortedMap()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(tr("Vorrat")) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = tr("Zurück"))
                    }
                },
                actions = {
                    if (sync.isSyncing) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                }
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            item {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    placeholder = { Text(tr("Suchen...")) },
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
            }
            if (visible.isEmpty()) {
                item {
                    Text(
                        if (query.isEmpty()) tr("Kein Vorrat erfasst") else tr("Nichts gefunden für „%s“", query),
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 32.dp)
                    )
                }
            } else if (query.isEmpty()) {
                item { Summary(visible) }
            }
            if (low.isNotEmpty()) {
                item { GroupTitle(tr("Nachkaufen")) }
                items(low, key = { "low-" + it.item.id }) { entry -> StockRow(entry, state) { navController.navigate("item/${entry.item.id}") } }
                item {
                    Text(
                        tr("Keine volle Spule mehr im Vorrat und angebrochen zusammen unter %s g.", Stock.LOW_THRESHOLD.toInt()),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
            groups.forEach { (type, entries) ->
                item(key = "type-$type") { GroupTitle(type) }
                items(entries, key = { it.item.id }) { entry -> StockRow(entry, state) { navController.navigate("item/${entry.item.id}") } }
            }
        }
    }
}

@Composable
private fun GroupTitle(text: String) {
    Text(text, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 8.dp))
}

@Composable
private fun Summary(entries: List<StockEntry>) {
    val full = entries.sumOf { it.fullSpools }
    val opened = entries.sumOf { it.openedSpools.size }
    val kilos = entries.mapNotNull { it.totalGrams }.sum() / 1000
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(tr("%s Sorten", entries.size), style = MaterialTheme.typography.titleMedium)
            Text(
                tr("%s volle, %s angebrochene Spulen · etwa %s kg", full, opened, String.format("%.1f", kilos)),
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
private fun StockRow(entry: StockEntry, state: StoreState, onClick: () -> Unit) {
    Card(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
    ) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.Top) {
            ItemHeader(entry.item, detail(entry, state), Modifier.weight(1f))
            entry.totalGrams?.let {
                Text(
                    GramText.label(it),
                    color = if (entry.isLow()) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 8.dp)
                )
            }
        }
    }
}

private fun detail(entry: StockEntry, state: StoreState): String {
    val parts = mutableListOf<String>()
    if (entry.item.spoolsInStock != null) {
        parts += if (entry.fullSpools == 1) tr("1 volle Spule") else tr("%s volle Spulen", entry.fullSpools)
    }
    entry.openedSpools.forEach { spool ->
        val rest = spool.remainingWeight?.let { " " + GramText.label(it) } ?: ""
        parts += "#${spool.number}$rest (${spool.locationText(state.printers, state.containers)})"
    }
    entry.item.transmissionText()?.let { parts += "TD $it" }
    return if (parts.isEmpty()) tr("Kein Vorrat erfasst") else parts.joinToString(" · ")
}
