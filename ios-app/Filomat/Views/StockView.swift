import SwiftUI

/// Vorrat aller Sorten: volle Spulen, angebrochene mit Restgewicht und Platz, und was knapp wird.
struct StockView: View {
    @Environment(ContainerStore.self) private var store
    @State private var searchQuery = ""
    @State private var itemToEdit: Item?

    private struct TypeGroup: Identifiable {
        var type: String
        var entries: [StockEntry]
        var id: String { type }
    }

    private var entries: [StockEntry] {
        let all = Stock.entries(items: store.items, spools: store.spools)
        let query = searchQuery.trimmingCharacters(in: .whitespaces).lowercased()
        guard !query.isEmpty else { return all }
        return all.filter { $0.item.displayTitle.lowercased().contains(query) || $0.item.name.lowercased().contains(query) }
    }

    var body: some View {
        let visible = entries
        let low = visible.filter { $0.isLow() }
        let groups = Dictionary(grouping: visible) { $0.item.itemData?.type ?? "Ohne Typ" }
            .map { TypeGroup(type: $0.key, entries: $0.value) }
            .sorted { $0.type < $1.type }

        List {
            if searchQuery.isEmpty, !visible.isEmpty {
                Section {
                    summary(of: visible)
                }
            }
            if !low.isEmpty {
                Section {
                    ForEach(low) { row(for: $0) }
                } header: {
                    Text("Nachkaufen")
                } footer: {
                    Text("Keine volle Spule mehr im Vorrat und angebrochen zusammen unter \(Int(Stock.lowThreshold)) g.")
                }
            }
            ForEach(groups) { group in
                Section(group.type) {
                    ForEach(group.entries) { row(for: $0) }
                }
            }
        }
        .overlay {
            if visible.isEmpty {
                if searchQuery.isEmpty {
                    ContentUnavailableView("Kein Vorrat erfasst", systemImage: "shippingbox")
                } else {
                    ContentUnavailableView.search(text: searchQuery)
                }
            }
        }
        .searchable(text: $searchQuery, prompt: "Suchen...")
        .refreshable { await store.refresh() }
        .navigationTitle("Vorrat")
        .sheet(item: $itemToEdit) { item in
            FilamentFormView(mode: .edit(item))
        }
    }

    private func summary(of entries: [StockEntry]) -> some View {
        let full = entries.reduce(0) { $0 + $1.fullSpools }
        let opened = entries.reduce(0) { $0 + $1.openedSpools.count }
        let grams = entries.compactMap(\.totalGrams).reduce(0, +)
        return VStack(alignment: .leading, spacing: 4) {
            Text("\(entries.count) Sorten").font(.headline)
            Text("\(full) volle, \(opened) angebrochene Spulen · etwa \((grams / 1000).formatted(.number.precision(.fractionLength(1)))) kg")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
    }

    private func row(for entry: StockEntry) -> some View {
        Button {
            itemToEdit = entry.item
        } label: {
            HStack(alignment: .top) {
                ItemHeader(item: entry.item, detail: detail(of: entry))
                Spacer()
                if let grams = entry.totalGrams {
                    Text("\(Int(grams.rounded())) g")
                        .monospacedDigit()
                        .foregroundStyle(entry.isLow() ? Color.orange : Color.secondary)
                }
            }
        }
        .foregroundStyle(.primary)
    }

    private func detail(of entry: StockEntry) -> String {
        var parts: [String] = []
        if entry.item.spoolsInStock != nil {
            parts.append(entry.fullSpools == 1 ? "1 volle Spule" : "\(entry.fullSpools) volle Spulen")
        }
        for spool in entry.openedSpools {
            let rest = spool.remainingWeight.map { " \(Int($0.rounded())) g" } ?? ""
            parts.append("#\(spool.number)\(rest) (\(spool.locationText(printers: store.printers, containers: store.containers)))")
        }
        if let td = entry.item.transmissionText { parts.append("TD \(td)") }
        return parts.isEmpty ? "Kein Vorrat erfasst" : parts.joined(separator: " · ")
    }
}
