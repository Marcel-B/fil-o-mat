import SwiftUI

/// Alle angebrochenen Spulen, getrennt von den Sorten unter "Filamente" (wie die Seite "Spulen" der Web-Oberfläche).
/// Antippen öffnet die Spule: wiegen, umstecken, als leer markieren, von dort weiter zur Sorte.
struct SpoolsView: View {
    @Environment(ContainerStore.self) private var store
    @State private var searchQuery = ""
    @State private var filter = SpoolFilter.all

    var body: some View {
        let entries = SpoolListEntry.entries(spools: store.spools, items: store.items, printers: store.printers,
                                             containers: store.containers, query: searchQuery, filter: filter)

        List {
            Section {
                Picker("Anzeigen", selection: $filter) {
                    ForEach(SpoolFilter.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())
            }

            Section {
                ForEach(entries) { entry in
                    NavigationLink {
                        SpoolEditView(spool: entry.spool, emptySpoolWeight: entry.item?.spoolWeight, showsItem: true)
                    } label: {
                        SpoolListRow(entry: entry)
                    }
                }
            } footer: {
                if !entries.isEmpty {
                    Text(store.spools.count == 1 ? String(localized: "1 Spule") : String(localized: "\(store.spools.count) Spulen"))
                }
            }
        }
        .overlay {
            if entries.isEmpty {
                if store.backend == nil {
                    ContentUnavailableView("Kein Backend verbunden", systemImage: "icloud.slash",
                                           description: Text("Angebrochene Spulen führt das Backend."))
                } else if !searchQuery.isEmpty {
                    ContentUnavailableView.search(text: searchQuery)
                } else {
                    ContentUnavailableView("Keine Spulen", systemImage: "circle.dotted",
                                           description: Text("Spulen brichst du beim Einlagern oder unter „Filamente“ an."))
                }
            }
        }
        .refreshable { await store.refresh() }
        .searchable(text: $searchQuery, prompt: "Suchen...")
        .navigationTitle("Spulen")
    }
}

private struct SpoolListRow: View {
    var entry: SpoolListEntry

    var body: some View {
        HStack(spacing: 12) {
            ColorDot(hex: entry.item?.itemData?.colorHex, size: 32)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline) {
                    Text(entry.title).font(.headline).lineLimit(2)
                    Text("#\(entry.spool.number)").font(.headline).monospacedDigit()
                    Spacer()
                    // Grau: nicht gewogen, das Backend rechnet mit einer vollen Spule
                    Text(entry.remaining.map { "\(Int($0.rounded())) g" } ?? String(localized: "Rest unbekannt"))
                        .monospacedDigit()
                        .foregroundStyle(entry.isWeighed ? .primary : .secondary)
                }
                Text([entry.item?.itemData?.color, entry.place].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                if let fraction = entry.fraction {
                    ProgressView(value: fraction)
                        .tint(fraction < 0.1 ? .red : fraction < 0.25 ? .orange : .accentColor)
                }
            }
        }
        .padding(.vertical, 2)
    }
}
