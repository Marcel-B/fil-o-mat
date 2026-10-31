package com.filomat.app.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Inventory
import androidx.compose.material.icons.filled.Print
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.ContainerStore
import com.filomat.app.ui.components.MainNavigationBar
import com.filomat.app.ui.components.MainTab
import com.filomat.app.i18n.tr

/** Scannen-Tab: eine Liste der Dinge, die man mit einem Tag tun kann. Jede führt in einen eigenen Ablauf. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ScanScreen(navController: NavController) {
    val context = LocalContext.current
    val store = remember { ContainerStore.get(context) }
    val sync by store.sync.collectAsState()

    Scaffold(
        topBar = { TopAppBar(title = { Text(tr("Scannen")) }) },
        bottomBar = { MainNavigationBar(navController, MainTab.SCAN) }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            item { SectionTitle(tr("Nachsehen")) }
            item {
                ScanActionRow(tr("Filament-Tag lesen"), tr("Alles, was auf dem Tag steht, und was Fil-o-mat zur Sorte weiß"), Icons.Default.Info) {
                    navController.navigate("read")
                }
            }
            item {
                ScanActionRow(tr("Container ansehen"), tr("Container scannen und sehen, was darin liegt"), Icons.Default.Archive) {
                    navController.navigate("scan/container")
                }
            }

            item { SectionTitle(tr("Filament bewegen")) }
            item {
                ScanActionRow(tr("Einlagern"), tr("Filament scannen, dann den Container"), Icons.Default.Download) {
                    navController.navigate("scan/place/container")
                }
            }
            item {
                ScanActionRow(
                    tr("In Drucker einlegen"), tr("Filament scannen, dann Drucker und Kopf wählen"), Icons.Default.Print,
                    enabled = sync.connected
                ) {
                    navController.navigate("scan/place/printer")
                }
            }
            if (!sync.connected) {
                item {
                    Text(
                        tr("Spulen in Druckern verwaltet das Backend. Verbinde es über das Wolken-Symbol in der Container-Liste."),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }

            item { SectionTitle(tr("Vorrat")) }
            item {
                ScanActionRow(tr("Vorrat"), tr("Volle und angebrochene Spulen je Sorte, was nachgekauft werden sollte"), Icons.Default.Inventory) {
                    navController.navigate("stock")
                }
            }
            item {
                ScanActionRow(tr("Lichtdurchlässigkeit erfassen"), tr("Musterplättchen scannen und den TD-Wert eintragen"), Icons.Default.WbSunny) {
                    navController.navigate("scan/transmission")
                }
            }
        }
    }
}

@Composable
private fun SectionTitle(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 8.dp)
    )
}

@Composable
private fun ScanActionRow(title: String, detail: String, icon: ImageVector, enabled: Boolean = true, onClick: () -> Unit) {
    Card(
        Modifier
            .fillMaxWidth()
            .clickable(enabled = enabled, onClick = onClick)
    ) {
        ListItem(
            headlineContent = { Text(title) },
            supportingContent = { Text(detail) },
            leadingContent = {
                Icon(icon, contentDescription = null, tint = if (enabled) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outline)
            },
            colors = ListItemDefaults.colors(
                containerColor = androidx.compose.ui.graphics.Color.Transparent,
                headlineColor = if (enabled) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.outline
            )
        )
    }
}
