import SwiftUI

struct ContainerListView: View {
    @Environment(ContainerStore.self) private var store
    @State private var showingAddContainer = false
    @State private var showingBackend = false
    @State private var showingSettings = false

    var body: some View {
        let regular = store.containers.filter { !$0.isStandard }
        let printerContainers = regular.filter { store.printer(owningContainer: $0.id) != nil }
        let otherContainers = regular.filter { store.printer(owningContainer: $0.id) == nil }
        // Der Standard-Container "Drucker" ist leer, seit jeder Drucker seinen eigenen Container hat
        let standard = store.containers.filter {
            $0.isStandard && ($0.id != Container.printerId || store.spoolCount(in: $0.id) > 0)
        }

        List {
            if let error = store.syncError {
                Section {
                    Label(error, systemImage: "exclamationmark.icloud")
                        .foregroundStyle(.red)
                        .onTapGesture { showingBackend = true }
                }
            }
            if let message = store.compatibility?.message {
                Section {
                    Button {
                        showingBackend = true
                    } label: {
                        Label(message, systemImage: "exclamationmark.arrow.triangle.2.circlepath")
                            .foregroundStyle(.orange)
                    }
                }
            }
            if let update = store.updateStatus, update.updateAvailable || update.state == "failed" {
                Section {
                    Button {
                        showingBackend = true
                    } label: {
                        if update.state == "failed" {
                            Label("Backend-Update fehlgeschlagen", systemImage: "exclamationmark.triangle")
                                .foregroundStyle(.red)
                        } else if update.isUpdating {
                            Label("Backend wird aktualisiert …", systemImage: "arrow.triangle.2.circlepath")
                        } else {
                            Label("Neue Backend-Version verfügbar", systemImage: "arrow.down.circle")
                        }
                    }
                }
            }
            if !printerContainers.isEmpty {
                Section("Drucker") {
                    ForEach(printerContainers) { ContainerRow(container: $0) }
                }
            }
            if !otherContainers.isEmpty {
                Section("Container") {
                    ForEach(otherContainers) { ContainerRow(container: $0) }
                }
            }
            if !standard.isEmpty {
                Section {
                    ForEach(standard) { ContainerRow(container: $0) }
                }
            }
        }
        .overlay {
            if store.containers.isEmpty {
                ContentUnavailableView("Keine Container vorhanden", systemImage: "archivebox")
            }
        }
        .refreshable { await store.refresh() }
        .navigationTitle("Container")
        .navigationDestination(for: Container.ID.self) { id in
            ContainerDetailView(containerId: id)
        }
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    showingBackend = true
                } label: {
                    Label("Backend", systemImage: store.backend == nil ? "icloud.slash" : "icloud")
                }
            }
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    showingSettings = true
                } label: {
                    Label("Einstellungen", systemImage: "gearshape")
                }
            }
            ToolbarItem(placement: .primaryAction) {
                Button {
                    showingAddContainer = true
                } label: {
                    Label("Hinzufügen", systemImage: "plus")
                }
            }
        }
        .sheet(isPresented: $showingAddContainer) {
            AddContainerView()
        }
        .sheet(isPresented: $showingBackend) {
            BackendSettingsView()
        }
        .sheet(isPresented: $showingSettings) {
            AppSettingsView()
        }
    }
}

private struct ContainerRow: View {
    @Environment(ContainerStore.self) private var store
    var container: Container

    var body: some View {
        NavigationLink(value: container.id) {
            VStack(alignment: .leading, spacing: 4) {
                Text(container.name).font(.title3)
                Text("RFID: \(container.rfidTag)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Text(store.spoolCount(in: container.id) == 1 ? "1 Spule" : "\(store.spoolCount(in: container.id)) Spulen")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if let printer = store.printer(owningContainer: container.id), printer.enabled,
                   let live = store.printerLive[printer.id] {
                    PrinterStatusLine(live: live)
                }
            }
            .padding(.vertical, 4)
        }
    }
}
