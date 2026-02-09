package com.filomat.app.ui.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import com.filomat.app.data.Item
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class ContainerDetailViewModel : ViewModel() {
    private val _container = MutableStateFlow<Container?>(null)
    val container: StateFlow<Container?> = _container
    
    fun updateContainer(container: Container) {
        _container.value = container
    }
    
    fun updateContainer(container: Container, store: ContainerStore) {
        viewModelScope.launch {
            store.updateContainer(container)
        }
    }
    
    fun addItem(item: Item, store: ContainerStore) {
        viewModelScope.launch {
            store.addItem(item)
            val containerId = item.containerId
            if (containerId != null) {
                store.addItemToContainer(item.id, containerId)
            }
            // Aktualisiere lokalen Container
            _container.value?.let { cont ->
                val updated = cont.copy(items = (cont.items + item).toMutableList())
                _container.value = updated
            }
        }
    }
    
    fun removeContainer(containerId: String, store: ContainerStore) {
        viewModelScope.launch {
            store.removeContainer(containerId)
            _container.value = null
        }
    }
    
    fun removeItemCompletely(itemId: String, store: ContainerStore) {
        viewModelScope.launch {
            store.removeItem(itemId)
            // Aktualisiere lokalen Container
            _container.value?.let { cont ->
                val updated = cont.copy(items = cont.items.filter { it.id != itemId }.toMutableList())
                _container.value = updated
            }
        }
    }
    
    fun moveItemToLose(itemId: String, currentContainerId: String, store: ContainerStore) {
        viewModelScope.launch {
            // Finde den "Lose" Container
            val loseContainerId = "standard_lose"
            // Verwende atomare Verschiebe-Funktion, um Datenverlust zu vermeiden
            store.moveItemToContainer(itemId, currentContainerId, loseContainerId)
            // Aktualisiere lokalen Container
            _container.value?.let { cont ->
                val updated = cont.copy(items = cont.items.filter { it.id != itemId }.toMutableList())
                _container.value = updated
            }
        }
    }
}
