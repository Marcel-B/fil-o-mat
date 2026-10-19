package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
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
import com.filomat.app.data.PlaceFlow
import com.filomat.app.data.sync.StoreState
import com.filomat.app.ui.components.GramText
import com.filomat.app.ui.components.ItemHeader
import com.filomat.app.ui.components.ScanButton
import com.filomat.app.ui.components.SectionCard
import com.filomat.app.ui.components.TransmissionField
import com.filomat.app.ui.components.rememberNfcReader
import kotlinx.coroutines.launch

/** Musterplättchen scannen und die gemessene Lichtdurchlässigkeit (TD) eintragen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TransmissionScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val state by store.state.collectAsState(initial = StoreState())
    val reader = rememberNfcReader()
    val scope = rememberCoroutineScope()
    var itemId by remember { mutableStateOf<String?>(null) }
    var value by remember { mutableStateOf("") }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var savedMessage by remember { mutableStateOf<String?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Lichtdurchlässigkeit") },
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
            ScanButton(reader, if (itemId == null) "Musterplättchen scannen" else "Nächstes scannen") { tag ->
                val tagId = tag.tagId.trim()
                savedMessage = null
                val container = state.findContainerByTag(tagId)
                if (container != null) {
                    errorMessage = "Das ist der Container „${container.name}“. Scanne das Musterplättchen."
                    return@ScanButton
                }
                val item = state.findItemByTag(tagId)
                    ?: Item(rfidTag = tagId, name = PlaceFlow.defaultName(tag.itemData), itemData = tag.itemData)
                        .also { scope.launch { store.addItem(it) } }
                itemId = item.id
                value = GramText.format(item.transmissionDistance)
                errorMessage = null
            }
            Text(
                "TD (Transmission Distance, HueForge) in mm, wie ihn z. B. ein TD-1 anzeigt. Je höher, desto mehr Licht lässt das Filament durch.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            savedMessage?.let {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Icon(Icons.Default.CheckCircle, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                    Text(it)
                }
            }

            val item = itemId?.let { state.item(it) }
            if (item != null) {
                SectionCard {
                    ItemHeader(item, item.transmissionText()?.let { "Bisher: $it" } ?: "Noch nicht gemessen")
                    TransmissionField(value) { value = it }
                    Button(
                        enabled = GramText.parse(value) != null,
                        onClick = {
                            val updated = item.copy(transmissionDistance = GramText.parse(value))
                            scope.launch { store.updateItem(updated) }
                            savedMessage = "${item.getDisplayTitle()}: TD ${updated.transmissionText() ?: ""}"
                            itemId = null
                            value = ""
                        },
                        modifier = Modifier.fillMaxWidth()
                    ) { Text("Speichern") }
                }
            }

            errorMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        }
    }
}
