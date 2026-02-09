package com.filomat.app.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.filomat.app.data.Container

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditContainerDialog(
    container: Container,
    onDismiss: () -> Unit,
    onSave: (Container) -> Unit
) {
    var name by remember { mutableStateOf(container.name) }
    var description by remember { mutableStateOf(container.description) }
    
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Container bearbeiten") },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text("Name") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true
                )
                
                OutlinedTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = { Text("Beschreibung") },
                    modifier = Modifier.fillMaxWidth()
                )
                
                OutlinedTextField(
                    value = container.rfidTag,
                    onValueChange = { },
                    label = { Text("RFID-Tag") },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = false,
                    singleLine = true
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val updated = container.copy(
                        name = name,
                        description = description
                    )
                    onSave(updated)
                },
                enabled = name.isNotBlank()
            ) {
                Text("Speichern")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Abbrechen")
            }
        }
    )
}
