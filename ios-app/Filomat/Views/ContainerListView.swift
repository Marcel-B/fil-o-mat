import SwiftUI

struct ContainerListView: View {
    @Environment(ContainerStore.self) private var store
    @State private var showingAddContainer = false

    var body: some View {
        let regular = store.containers.filter { !$0.isStandard }
        let standard = store.containers.filter { $0.isStandard }

        List {
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
        .navigationTitle("Container")
        .navigationDestination(for: Container.ID.self) { id in
            ContainerDetailView(containerId: id)
        }
        .toolbar {
            Button {
                showingAddContainer = true
            } label: {
                Label("Hinzufügen", systemImage: "plus")
            }
        }
        .sheet(isPresented: $showingAddContainer) {
            AddContainerView()
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
                Text("\(store.items(in: container.id).count) Filamente")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .padding(.vertical, 4)
        }
    }
}
