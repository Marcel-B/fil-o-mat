import SwiftUI

/// Container scannen und sehen, was darin liegt: Sorten, angebrochene Spulen bzw. bei einem Drucker die Spulen je Kopf.
struct ContainerScanView: View {
    @Environment(ContainerStore.self) private var store
    @State private var containerId: String?
    @State private var errorMessage: String?

    var body: some View {
        List {
            Section {
                ScanButton(title: containerId == nil ? "Container scannen" : "Nächsten Container scannen",
                           prompt: "Halten Sie das iPhone an den Container-Tag.") { tag in
                    handle(tag)
                } onError: { errorMessage = $0 }
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
                if let errorMessage {
                    Text(errorMessage).foregroundStyle(.red)
                }
            }

            if let containerId, let container = store.container(id: containerId) {
                if let printer = store.printer(owningContainer: container.id) {
                    printerSection(printer)
                } else {
                    contentSections(container)
                }
            }
        }
        .navigationTitle("Container ansehen")
    }

    private func printerSection(_ printer: Printer) -> some View {
        Section("Drucker \(printer.name)") {
            ForEach(0..<printer.toolCount, id: \.self) { tool in
                if let spool = store.spool(inPrinter: printer.id, tool: tool) {
                    LabeledContent(printer.toolCount > 1 ? printer.placeName(tool: tool) : "Eingelegt") {
                        VStack(alignment: .trailing) {
                            Text("\(store.item(id: spool.itemId)?.displayTitle ?? "Filament") #\(spool.number)")
                            if let grams = spool.remainingWeight {
                                Text("\(Int(grams.rounded())) g").font(.caption).foregroundStyle(.secondary)
                            }
                        }
                    }
                } else {
                    LabeledContent(printer.toolCount > 1 ? printer.placeName(tool: tool) : "Eingelegt", value: "frei")
                }
            }
        }
    }

    @ViewBuilder
    private func contentSections(_ container: Container) -> some View {
        let items = store.unloadedItems(in: container.id)
        let spools = store.spools(inContainer: container.id)

        Section {
            if !container.description.isEmpty {
                Text(container.description).foregroundStyle(.secondary)
            }
            NavigationLink("Container öffnen") {
                ContainerDetailView(containerId: container.id)
            }
        } header: {
            Text(container.name)
        }

        if !items.isEmpty || spools.isEmpty {
            Section("Sorten (\(items.count))") {
                if items.isEmpty {
                    Text("Keine Sorten in diesem Container").foregroundStyle(.secondary)
                }
                ForEach(items) { item in
                    ItemHeader(item: item, detail: item.spoolsInStock.map { "Vorrat: \($0)" })
                }
            }
        }

        if !spools.isEmpty {
            Section("Angebrochene Spulen (\(spools.count))") {
                ForEach(spools) { spool in
                    LabeledContent("\(store.item(id: spool.itemId)?.displayTitle ?? "Filament") #\(spool.number)",
                                   value: spool.remainingWeight.map { "\(Int($0.rounded())) g" } ?? "Rest unbekannt")
                }
            }
        }
    }

    private func handle(_ tag: ScannedTag) {
        let tagId = tag.tagId.trimmingCharacters(in: .whitespaces)
        if let container = store.findContainer(byTag: tagId) {
            containerId = container.id
            errorMessage = nil
        } else if store.findItem(byTag: tagId) != nil {
            errorMessage = "Das ist ein Filament-Tag. Für Filamente gibt es „Filament-Tag lesen“."
        } else {
            errorMessage = "Dieser Container ist in Fil-o-mat noch nicht angelegt."
        }
    }
}
