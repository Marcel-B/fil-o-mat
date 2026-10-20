import SwiftUI

struct ContainerListView: View {
    @Environment(ContainerStore.self) private var store
    @State private var showingAddContainer = false
    @State private var showingBackend = false

    var body: some View {
        let regular = store.containers.filter { !$0.isStandard }
        let standard = store.containers.filter { $0.isStandard }

        List {
            if let error = store.syncError {
                Section {
                    Label(error, systemImage: "exclamationmark.icloud")
                        .foregroundStyle(.red)
                        .onTapGesture { showingBackend = true }
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
            if !regular.isEmpty {
                Section {
                    ForEach(regular) { ContainerRow(container: $0) }
                }
            }
            if !standard.isEmpty {
                Section("Standard-Container") {
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
                Text("\(store.filamentCount(in: container.id)) Filamente")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .padding(.vertical, 4)
        }
    }
}
