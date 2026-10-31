package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.filomat.app.data.AppLanguage
import com.filomat.app.data.AppSettings
import com.filomat.app.data.Appearance
import com.filomat.app.i18n.tr
import com.filomat.app.ui.components.SectionCard

/** Einstellungen der App selbst: Farbschema und Sprache. Beides wechselt sofort (die Activity startet neu). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(navController: NavController) {
    val context = LocalContext.current
    var appearance by remember { mutableStateOf(AppSettings.appearance(context)) }
    var language by remember { mutableStateOf(AppSettings.language()) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(tr("Einstellungen")) },
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
            SectionCard(title = tr("Darstellung")) {
                Column(Modifier.selectableGroup()) {
                    listOf(
                        Appearance.SYSTEM to tr("Wie das System"),
                        Appearance.LIGHT to tr("Hell"),
                        Appearance.DARK to tr("Dunkel")
                    ).forEach { (option, label) ->
                        ChoiceRow(label, selected = appearance == option) {
                            appearance = option
                            AppSettings.setAppearance(context, option)
                        }
                    }
                }
            }
            SectionCard(title = tr("Sprache")) {
                Column(Modifier.selectableGroup()) {
                    listOf(
                        AppLanguage.SYSTEM to tr("Wie das System"),
                        AppLanguage.GERMAN to "Deutsch",
                        AppLanguage.ENGLISH to "English"
                    ).forEach { (option, label) ->
                        ChoiceRow(label, selected = language == option) {
                            language = option
                            AppSettings.setLanguage(option)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ChoiceRow(label: String, selected: Boolean, onSelect: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .selectable(selected = selected, onClick = onSelect, role = Role.RadioButton)
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = selected, onClick = null)
        Spacer(Modifier.width(12.dp))
        Text(label)
    }
}
