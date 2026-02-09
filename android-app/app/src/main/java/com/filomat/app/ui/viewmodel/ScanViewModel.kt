package com.filomat.app.ui.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import com.filomat.app.ui.screens.ScanMode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class ScanViewModel : ViewModel() {
    private val _scanMode = MutableStateFlow(ScanMode.ADD_TO_CONTAINER)
    val scanMode: StateFlow<ScanMode> = _scanMode
    
    private val _scannedContainer = MutableStateFlow<Container?>(null)
    val scannedContainer: StateFlow<Container?> = _scannedContainer
    
    private val _scannedItem = MutableStateFlow<Item?>(null)
    val scannedItem: StateFlow<Item?> = _scannedItem
    
    private val _currentStep = MutableStateFlow(0)
    val currentStep: StateFlow<Int> = _currentStep
    
    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage
    
    private val _warningMessage = MutableStateFlow<String?>(null)
    val warningMessage: StateFlow<String?> = _warningMessage
    
    private val _showConfirmDialog = MutableStateFlow(false)
    val showConfirmDialog: StateFlow<Boolean> = _showConfirmDialog
    
    fun setScanMode(mode: ScanMode) {
        _scanMode.value = mode
        resetScan()
    }
    
    fun handleScannedTag(tag: String, itemData: com.filomat.app.data.ItemJsonData?, store: ContainerStore) {
        viewModelScope.launch {
            if (_scanMode.value == ScanMode.ADD_TO_CONTAINER) {
                if (_currentStep.value == 0) {
                    // Container scannen - prüfe ob bekannt
                    val container = store.findContainerByTag(tag)
                    if (container != null) {
                        _scannedContainer.value = container
                        _currentStep.value = 1
                        _errorMessage.value = null
                    } else {
                        _errorMessage.value = "Container nicht gefunden. Bitte erstellen Sie den Container zuerst."
                        _scannedContainer.value = null
                    }
                } else {
                    // Item scannen
                    val item = store.findItemByTag(tag)
                    if (item != null) {
                        // Item existiert bereits - prüfe ob es in einem Container ist
                        val currentContainer = _scannedContainer.value
                        if (item.containerId != null) {
                            if (item.containerId == currentContainer?.id) {
                                // Item ist bereits in diesem Container
                                _warningMessage.value = "Dieses Item ist bereits in diesem Container."
                                _scannedItem.value = null
                            } else {
                                // Item ist in einem anderen Container - zeige Bestätigungsdialog
                                _scannedItem.value = item
                                _showConfirmDialog.value = true
                            }
                        } else {
                            // Item ist noch nicht in einem Container
                            _scannedItem.value = item
                            _warningMessage.value = null
                        }
                    } else {
                        // Item existiert nicht - erstelle es mit den gescannten Daten
                        val newItem = com.filomat.app.data.Item(
                            rfidTag = tag,
                            name = "${itemData?.brand ?: ""} ${itemData?.type ?: ""}".trim().ifEmpty { "Item" },
                            description = "",
                            itemData = itemData
                        )
                        store.addItem(newItem)
                        _scannedItem.value = newItem
                        _warningMessage.value = null
                    }
                }
            } else {
                if (_currentStep.value == 0) {
                    // Item scannen
                    val item = store.findItemByTag(tag)
                    if (item != null) {
                        _scannedItem.value = item
                    } else {
                        // Item existiert nicht - erstelle es
                        val newItem = com.filomat.app.data.Item(
                            rfidTag = tag,
                            name = "${itemData?.brand ?: ""} ${itemData?.type ?: ""}".trim().ifEmpty { "Item" },
                            description = "",
                            itemData = itemData
                        )
                        store.addItem(newItem)
                        _scannedItem.value = newItem
                    }
                    _currentStep.value = 1
                } else {
                    // Container scannen - prüfe ob bekannt
                    val container = store.findContainerByTag(tag)
                    if (container != null) {
                        _scannedContainer.value = container
                        _errorMessage.value = null
                    } else {
                        _errorMessage.value = "Container nicht gefunden. Bitte erstellen Sie den Container zuerst."
                        _scannedContainer.value = null
                    }
                }
            }
        }
    }
    
    fun performAction(store: ContainerStore, onStopScanning: () -> Unit) {
        viewModelScope.launch {
            val container = _scannedContainer.value
            val item = _scannedItem.value
            
            if (container != null && item != null) {
                if (_scanMode.value == ScanMode.ADD_TO_CONTAINER) {
                    // Verwende atomare Verschiebe-Funktion, um Datenverlust zu vermeiden
                    if (item.containerId != null && item.containerId != container.id) {
                        store.moveItemToContainer(item.id, item.containerId, container.id)
                    } else {
                        store.addItemToContainer(item.id, container.id)
                    }
                } else {
                    store.removeItemFromContainer(item.id, container.id)
                }
            }
            
            onStopScanning()
            resetScan()
        }
    }
    
    fun confirmMoveItem() {
        _showConfirmDialog.value = false
    }
    
    fun cancelMoveItem() {
        _showConfirmDialog.value = false
        _scannedItem.value = null
        _warningMessage.value = null
    }
    
    private fun resetScan() {
        _scannedContainer.value = null
        _scannedItem.value = null
        _currentStep.value = 0
        _errorMessage.value = null
        _warningMessage.value = null
        _showConfirmDialog.value = false
    }
}
