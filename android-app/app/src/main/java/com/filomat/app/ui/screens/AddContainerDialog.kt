package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import com.filomat.app.data.Container
import com.filomat.app.nfc.NFCReader
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

@OptIn(ExperimentalMaterial3Api::class, ExperimentalSerializationApi::class)
@Composable
fun AddContainerDialog(
    onDismiss: () -> Unit,
    onAdd: (Container) -> Unit
) {
    val context = LocalContext.current
    val nfcReader = remember { NFCReader(context as android.app.Activity) }
    val scope = rememberCoroutineScope()

    var name by remember { mutableStateOf("") }
    var description by remember { mutableStateOf("") }
    var capacityText by remember { mutableStateOf("") }
    var isProcessing by remember { mutableStateOf(false) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusError by remember { mutableStateOf<String?>(null) }

    val isNfcAvailable by remember { mutableStateOf(nfcReader.isAvailable) }
    val errorMessage by nfcReader.errorMessage.collectAsState()

    val capacity: Int? = capacityText.toIntOrNull()

    @Serializable
    data class ContainerJsonData(
        val name: String,
        val description: String,
        val capacity: String
    )

    // Zustand für den Workflow: 0 = Eingabe, 1 = Schreiben, 2 = Lesen
    var workflowState by remember { mutableStateOf(0) }

    AlertDialog(
        onDismissRequest = {
            // Erlaube Abbrechen auch während des Schreib-/Lesevorgangs
            nfcReader.stopScanning()
            isProcessing = false
            onDismiss()
        },
        title = {
            Text(
                when (workflowState) {
                    1 -> "Tag beschreiben"
                    2 -> "Tag lesen"
                    else -> "Neuer Container"
                }
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                when (workflowState) {
                    0 -> {
                        // Eingabe-Phase
                        OutlinedTextField(
                            value = name,
                            onValueChange = { name = it },
                            label = { Text("Name *") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            enabled = !isProcessing
                        )

                        OutlinedTextField(
                            value = description,
                            onValueChange = { description = it },
                            label = { Text("Beschreibung") },
                            modifier = Modifier.fillMaxWidth(),
                            enabled = !isProcessing
                        )

                        OutlinedTextField(
                            value = capacityText,
                            onValueChange = { capacityText = it },
                            label = { Text("Menge") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            placeholder = { Text("Optional") },
                            enabled = !isProcessing,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            supportingText = {
                                if (capacityText.isNotBlank() && capacity == null) {
                                    Text("Bitte eine gültige Zahl eingeben", color = MaterialTheme.colorScheme.error)
                                }
                            }
                        )

                        if (!isNfcAvailable) {
                            Text(
                                text = "NFC ist auf diesem Gerät nicht verfügbar.",
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                    1 -> {
                        // Schreib-Phase
                        Text(
                            text = "Bitte halten Sie ein RFID-Tag an Ihr Gerät, um es zu beschreiben.",
                            style = MaterialTheme.typography.bodyMedium
                        )
                        if (isProcessing) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.Center
                            ) {
                                CircularProgressIndicator(modifier = Modifier.size(24.dp))
                                Spacer(modifier = Modifier.width(8.dp))
                                Text("Schreibe Tag...")
                            }
                        }
                    }
                    2 -> {
                        // Lese-Phase
                        Text(
                            text = "Bitte halten Sie das beschriebene Tag erneut an Ihr Gerät, um es zu lesen.",
                            style = MaterialTheme.typography.bodyMedium
                        )
                        if (isProcessing) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.Center
                            ) {
                                CircularProgressIndicator(modifier = Modifier.size(24.dp))
                                Spacer(modifier = Modifier.width(8.dp))
                                Text("Lese Tag...")
                            }
                        }
                    }
                }

                statusMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.primary,
                        style = MaterialTheme.typography.bodySmall
                    )
                }

                statusError?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodySmall
                    )
                }

                errorMessage?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
            }
        },
        confirmButton = {
            when (workflowState) {
                0 -> {
                    TextButton(
                        onClick = {
                            if (name.isNotBlank() && isNfcAvailable) {
                                workflowState = 1
                                isProcessing = true
                                statusMessage = null
                                statusError = null

                                // Starte Schreibvorgang
                                val containerData = ContainerJsonData(
                                    name = name,
                                    description = description,
                                    capacity = capacity?.toString() ?: ""
                                )

                                val jsonData = try {
                                    Json.encodeToString(containerData)
                                } catch (e: Exception) {
                                    statusError = "Fehler beim Erstellen der JSON-Daten: ${e.message}"
                                    isProcessing = false
                                    workflowState = 0
                                    return@TextButton
                                }

                                nfcReader.startWriting { tag ->
                                    val success = nfcReader.writeJsonTag(jsonData, tag)
                                    if (success) {
                                        statusMessage = "Tag erfolgreich beschrieben! Bitte halten Sie das Tag erneut an das Gerät."
                                        workflowState = 2

                                        // Warte kurz, dann starte Lesevorgang
                                        scope.launch {
                                            // Stoppe den Writer-Mode zuerst
                                            nfcReader.stopScanning()

                                            // Warte, damit der NFC-Adapter sich zurücksetzt
                                            delay(800)

                                            // Starte Lesevorgang
                                            isProcessing = true
                                            nfcReader.startScanning { scannedTag ->
                                                // Container speichern
                                                val container = Container(
                                                    rfidTag = scannedTag,
                                                    name = name,
                                                    description = description,
                                                    capacity = capacity
                                                )
                                                onAdd(container)
                                                isProcessing = false
                                                nfcReader.stopScanning()
                                                onDismiss()
                                            }
                                        }
                                    } else {
                                        statusError = nfcReader.errorMessage.value ?: "Fehler beim Schreiben des Tags"
                                        isProcessing = false
                                        workflowState = 0
                                        nfcReader.stopScanning()
                                    }
                                }
                            }
                        },
                        enabled = name.isNotBlank() && isNfcAvailable && !isProcessing
                    ) {
                        Text("Weiter")
                    }
                }
                else -> null
            }
        },
        dismissButton = {
            TextButton(
                onClick = {
                    nfcReader.stopScanning()
                    if (workflowState == 0) {
                        onDismiss()
                    } else {
                        // Zurück zur Eingabe
                        workflowState = 0
                        isProcessing = false
                        statusMessage = null
                        statusError = null
                    }
                },
                enabled = true
            ) {
                Text(if (workflowState == 0) "Abbrechen" else "Zurück")
            }
        }
    )
}
