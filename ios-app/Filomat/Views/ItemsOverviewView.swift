import SwiftUI

struct ItemsOverviewView: View {
    @Environment(ContainerStore.self) private var store
    @State private var searchQuery = ""
    @State private var itemToEdit: Item?
    @State private var itemToDelete: Item?
    @State private var showingCreate = false

    private struct ItemGroup: Identifiable {
        var type: String
        var items: [Item]
        var id: String { type }
    }

    /// Gefilterte Filamente, gruppiert nach Typ und alphabetisch sortiert.
    private var groups: [ItemGroup] {
        let query = searchQuery.trimmingCharacters(in: .whitespaces).lowercased()
        let filtered = query.isEmpty ? store.items : store.items.filter { item in
            item.name.lowercased().contains(query)
                || item.itemData?.type?.lowercased().contains(query) == true
                || item.itemData?.brand?.lowercased().contains(query) == true
                || item.rfidTag.lowercased().contains(query)
        }
        return Dictionary(grouping: filtered) { $0.itemData?.type ?? "Ohne Typ" }
            .map { ItemGroup(type: $0.key, items: $0.value) }
            .sorted { $0.type < $1.type }
    }

    var body: some View {
        let visibleGroups = groups

        List {
            ForEach(visibleGroups) { group in
                Section("\(group.type) (\(group.items.count))") {
                    ForEach(group.items) { item in
                        ItemOverviewRow(item: item, containerName: containerName(for: item), spools: store.spools(of: item.id),
                                        isPossibleDuplicate: !store.possibleDuplicates(of: item).isEmpty)
                            .contentShape(Rectangle())
                            .onTapGesture { itemToEdit = item }
                            .swipeActions {
                                Button(role: .destructive) {
                                    itemToDelete = item
                                } label: {
                                    Label("Löschen", systemImage: "trash")
                                }
                                Button {
                                    itemToEdit = item
                                } label: {
                                    Label("Bearbeiten", systemImage: "pencil")
                                }
                                .tint(.accentColor)
                            }
                    }
                }
            }
        }
        .overlay {
            if visibleGroups.isEmpty {
                if searchQuery.isEmpty {
                    ContentUnavailableView("Keine Filamente vorhanden", systemImage: "circle.dotted")
                } else {
                    ContentUnavailableView.search(text: searchQuery)
                }
            }
        }
        .refreshable { await store.refresh() }
        .searchable(text: $searchQuery, prompt: "Suchen...")
        .navigationTitle("Filamente")
        .toolbar {
            Button {
                showingCreate = true
            } label: {
                Label("Filament erstellen", systemImage: "plus")
            }
        }
        .sheet(isPresented: $showingCreate) {
            FilamentFormView(mode: .create)
        }
        .sheet(item: $itemToEdit) { item in
            FilamentFormView(mode: .edit(item))
        }
        .alert(
            "Filament löschen",
            isPresented: Binding(get: { itemToDelete != nil }, set: { if !$0 { itemToDelete = nil } }),
            presenting: itemToDelete
        ) { item in
            Button("Löschen", role: .destructive) { store.removeItem(id: item.id) }
            Button("Abbrechen", role: .cancel) {}
        } message: { item in
            Text("Möchten Sie das Filament '\(item.displayTitle)' wirklich löschen?")
        }
    }

    private func containerName(for item: Item) -> String {
        item.containerId.flatMap { store.container(id: $0)?.name } ?? "Lose"
    }
}

private struct ItemOverviewRow: View {
    var item: Item
    var containerName: String
    var spools: [Spool] = []
    var isPossibleDuplicate = false

    var body: some View {
        HStack(spacing: 12) {
            ColorDot(hex: item.itemData?.colorHex, size: 40)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline) {
                    Text(item.displayTitle)
                        .font(.headline)
                        .lineLimit(2)
                    Spacer()
                    if let label = item.itemData?.colorLabel {
                        Text("(\(label))")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                }
                Group {
                    if let type = item.itemData?.type { field("Typ:", type) }
                    if let brand = item.itemData?.brand { field("Marke:", brand) }
                    field("Container:", containerName)
                    if !spools.isEmpty {
                        field("Angebrochen:", spools.map { spool in
                            spool.remainingWeight.map { "#\(spool.number) \(Int($0.rounded())) g" } ?? "#\(spool.number)"
                        }.joined(separator: ", "))
                    }
                    if let stock = item.spoolsInStock, stock > 0 {
                        field("Vorrat:", stock == 1 ? "1 Spule" : "\(stock) Spulen")
                    }
                    if let td = item.transmissionText { field("TD:", td) }
                    if isPossibleDuplicate {
                        Label("Möglicherweise doppelt", systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.orange)
                    }
                }
                .font(.caption)
            }
        }
        .padding(.vertical, 4)
    }

    private func field(_ label: String, _ value: String) -> some View {
        HStack(spacing: 4) {
            Text(label).foregroundStyle(.secondary)
            Text(value)
        }
    }
}
