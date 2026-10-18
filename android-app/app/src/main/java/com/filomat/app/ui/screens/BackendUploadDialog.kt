package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.filomat.app.data.BackendSync
import com.filomat.app.data.ContainerStore
import kotlinx.coroutines.launch

/** Lädt die lokal gespeicherten Container und Filamente ins Backend, damit z. B. die iOS-App sie sieht. */
@Composable
fun BackendUploadDialog(store: ContainerStore, onDismiss: () -> Unit) {
    val context = LocalContext.current
    val backend = remember { BackendSync(context) }
    val scope = rememberCoroutineScope()
    var serverUrl by remember { mutableStateOf(backend.serverUrl) }
    var apiKey by remember { mutableStateOf(backend.apiKey) }
    var isUploading by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }

    AlertDialog(
        onDismissRequest = { if (!isUploading) onDismiss() },
        title = { Text("Ins Backend hochladen") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Überträgt alle Container und Filamente dieses Geräts. Vorhandene Einträge werden aktualisiert, nichts wird gelöscht.")
                OutlinedTextField(
                    value = serverUrl,
                    onValueChange = { serverUrl = it },
                    label = { Text("Server-Adresse") },
                    placeholder = { Text("http://nas.local:8080") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = apiKey,
                    onValueChange = { apiKey = it },
                    label = { Text("API-Schlüssel (optional)") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth()
                )
                if (isUploading) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                message?.let { Text(it) }
            }
        },
        confirmButton = {
            Button(
                enabled = !isUploading && serverUrl.startsWith("http"),
                onClick = {
                    backend.serverUrl = serverUrl
                    backend.apiKey = apiKey
                    isUploading = true
                    message = null
                    scope.launch {
                        message = try {
                            val result = backend.upload(store)
                            buildString {
                                append("Hochgeladen: ${result.containers} Container, ${result.items} Filamente.")
                                result.skipped.forEach { append("\n• ").append(it) }
                            }
                        } catch (e: Exception) {
                            "Fehler: ${e.message ?: e.javaClass.simpleName}"
                        }
                        isUploading = false
                    }
                }
            ) { Text("Hochladen") }
        },
        dismissButton = {
            TextButton(enabled = !isUploading, onClick = onDismiss) { Text("Schließen") }
        }
    )
}
