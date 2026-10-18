package com.filomat.app.ui.components

import android.graphics.Color as AndroidColor
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Nfc
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

/** Hauptbereiche der App, wie die Tabs der iOS-App plus "Tag lesen". */
enum class MainTab(val route: String, val label: String, val icon: ImageVector) {
    CONTAINERS("containers", "Container", Icons.Default.Archive),
    SCAN("scan", "Scannen", Icons.Default.QrCodeScanner),
    READ("read", "Lesen", Icons.Default.Nfc),
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
