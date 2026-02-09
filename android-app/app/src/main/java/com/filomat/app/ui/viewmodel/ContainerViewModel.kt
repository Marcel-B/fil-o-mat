package com.filomat.app.ui.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.filomat.app.data.Container
import com.filomat.app.data.ContainerStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class ContainerViewModel : ViewModel() {
    private val _containers = MutableStateFlow<List<Container>>(emptyList())
    val containers: StateFlow<List<Container>> = _containers
    
    fun updateContainers(newContainers: List<Container>) {
        _containers.value = newContainers
    }
    
    fun addContainer(container: Container, store: ContainerStore) {
        viewModelScope.launch {
            store.addContainer(container)
        }
    }
    
    fun updateContainer(container: Container, store: ContainerStore) {
        viewModelScope.launch {
            store.updateContainer(container)
        }
    }
    
    fun removeContainer(containerId: String, store: ContainerStore) {
        viewModelScope.launch {
            store.removeContainer(containerId)
        }
    }
}
