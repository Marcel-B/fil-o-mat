import SwiftUI

struct ContainerDetailView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    var containerId: String

    @State private var showingEdit = false
    @State private var showingDelete = false
    @State private var itemToRemove: Item?
    @State private var itemToEdit: Item?
    @State private var isScanning = false
    @State private var scanMessage: String?
    @State private var showingItemPicker = false

    var body: some View {
        if let container = store.container(id: containerId) {
            content(for: container)
        } else {
            ContentUnavailableView("Container nicht gefunden", systemImage: "questionmark.folder")
        }
    }

    private func content(for container: Container) -> some View {
        let printer = store.printer(owningContainer: container.id)
        // Sorten, deren Spule in einem Kopf steckt, zeigt "Im Drucker"
        let items = store.unloadedItems(in: container.id)
        let isLoose = container.id == Container.looseId

        return List {
            Section("Informationen") {
                InfoRow(label: "Name", value: container.name)
                InfoRow(label: "RFID-Tag", value: container.rfidTag)
                if !container.description.isEmpty {
                    InfoRow(label: "Beschreibung", value: container.description)
                }
                if let capacity = container.capacity {
                    InfoRow(label: "Menge", value: "\(capacity)")
                }
            }

            // Der Container eines Druckers: was in seinen Köpfen oder Slots steckt
            if let printer {
                Section("Im Drucker") {
                    ForEach(0..<printer.toolCount, id: \.self) { tool in
                        if let spool = store.spool(inPrinter: printer.id, tool: tool) {
                            let item = store.item(id: spool.itemId)
                            NavigationLink {
                                SpoolEditView(spool: spool, emptySpoolWeight: item?.spoolWeight, showsItem: true)
                            } label: {
                                LabeledContent {
                                    // Ohne Restgewicht rechnet das Backend mit einer vollen Spule der Sorte
                                    Text((spool.remainingWeight ?? item?.netWeight).map { "\(Int($0.rounded())) g" } ?? "Rest unbekannt")
                                } label: {
                                    Text(printer.placeName(tool: tool))
                                    // Farbe dazu: zwei Sorten gleicher Marke und Art unterscheiden sich sonst nicht
                                    HStack(spacing: 6) {
                                        ColorDot(hex: item?.itemData?.colorHex, size: 12, showsPlaceholder: false)
                                        Text([item?.displayTitle ?? "Filament", item?.itemData?.color]
                                            .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ") + " #\(spool.number)")
                                    }
                                    .font(.subheadline)
                                    .foregroundStyle(.secondary)
                                }
                            }
                        } else {
                            LabeledContent(printer.placeName(tool: tool), value: "frei")
                        }
                    }
                }
            }

            // Im Drucker-Container stehen Sorten, die in einem Slot stecken, schon oben
            if printer == nil || !items.isEmpty {
                Section("Filamente (\(items.count))") {
                    if items.isEmpty {
                        Text("Keine Filamente in diesem Container").foregroundStyle(.secondary)
                    }
                    ForEach(items) { item in
                        ItemDetailRow(item: item, isLooseContainer: isLoose) {
                            itemToRemove = item
                        }
                        // Antippen öffnet die Sorte; der Minus-Knopf behält seinen eigenen Tipp
                        .contentShape(Rectangle())
                        .onTapGesture { itemToEdit = item }
                    }
                }
            }

            let spools = store.spools(inContainer: container.id)
            if !spools.isEmpty {
                Section("Angebrochene Spulen (\(spools.count))") {
                    ForEach(spools) { spool in
                        let item = store.item(id: spool.itemId)
                        NavigationLink {
                            SpoolEditView(spool: spool, emptySpoolWeight: item?.spoolWeight, showsItem: true)
                        } label: {
                            LabeledContent("\(item?.displayTitle ?? "Filament") #\(spool.number)",
                                           value: spool.remainingWeight.map { "\(Int($0.rounded())) g" } ?? "Rest unbekannt")
                        }
                    }
                }
            }
        }
        .navigationTitle(container.name)
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button {
                    // Ohne NFC (iPad, Mac) eine bekannte Sorte aus der Liste wählen
                    if NFCService.isAvailable {
                        Task { await scanItem(into: container) }
                    } else {
                        showingItemPicker = true
                    }
                } label: {
                    Label("Filament hinzufügen", systemImage: "plus")
                }
                .disabled(isScanning)

                Menu {
                    Button {
                        showingEdit = true
                    } label: {
                        Label("Bearbeiten", systemImage: "pencil")
                    }
                    // Standard-Container können nicht gelöscht werden
                    if !container.isStandard {
                        Button(role: .destructive) {
                            showingDelete = true
                        } label: {
                            Label("Löschen", systemImage: "trash")
                        }
                    }
                } label: {
                    Label("Mehr", systemImage: "ellipsis.circle")
                }
            }
        }
        .sheet(isPresented: $showingEdit) {
            EditContainerView(container: container)
        }
        .sheet(item: $itemToEdit) { item in
            FilamentFormView(mode: .edit(item))
        }
        .sheet(isPresented: $showingItemPicker) {
            TagPickerView(kind: .item, excludedItemIds: Set(store.items(in: container.id).map(\.id))) { tag in
                add(tag, to: container)
            }
        }
        .alert("Container löschen", isPresented: $showingDelete) {
            Button("Löschen", role: .destructive) {
                store.removeContainer(id: container.id)
                dismiss()
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Möchten Sie den Container '\(container.name)' wirklich löschen? Die Filamente werden in den Container 'Lose' verschoben.")
        }
        .confirmationDialog(
            isLoose ? "Filament löschen" : "Filament entfernen",
            isPresented: Binding(get: { itemToRemove != nil }, set: { if !$0 { itemToRemove = nil } }),
            titleVisibility: .visible,
            presenting: itemToRemove
        ) { item in
            if !isLoose {
                Button("In 'Lose' verschieben") {
                    store.moveItem(id: item.id, toContainer: Container.looseId)
                }
            }
            Button(isLoose ? "Löschen" : "Komplett löschen", role: .destructive) {
                store.removeItem(id: item.id)
            }
            Button("Abbrechen", role: .cancel) {}
        } message: { item in
            if isLoose {
                Text("Möchten Sie das Filament '\(item.displayTitle)' wirklich komplett löschen?")
            } else {
                Text("Was möchten Sie mit dem Filament '\(item.displayTitle)' machen?")
            }
        }
        .alert("Filament scannen", isPresented: Binding(get: { scanMessage != nil }, set: { if !$0 { scanMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(scanMessage ?? "")
        }
    }

    /// Filament-Tag scannen und in diesen Container legen (legt unbekannte Filamente neu an).
    private func scanItem(into container: Container) async {
        isScanning = true
        defer { isScanning = false }

        let tag: ScannedTag
        do {
            tag = try await NFCService.shared.scan(prompt: "Halten Sie das iPhone an ein Filament-Tag.")
        } catch NFCError.cancelled {
            return
        } catch {
            scanMessage = error.localizedDescription
            return
        }
        add(tag, to: container)
    }

    /// Legt das Filament eines gescannten oder gewählten Tags in diesen Container.
    private func add(_ tag: ScannedTag, to container: Container) {
        guard !tag.tagId.trimmingCharacters(in: .whitespaces).isEmpty else {
            scanMessage = "Ungültiger RFID-Tag."
            return
        }

        if let existing = store.findItem(byTag: tag.tagId) {
            if existing.containerId == container.id {
                scanMessage = "Dieses Filament ist bereits in diesem Container."
            } else {
                let wasInContainer = existing.containerId != nil
                store.moveItem(id: existing.id, toContainer: container.id)
                scanMessage = wasInContainer
                    ? "Filament wurde in diesen Container verschoben."
                    : "Filament wurde hinzugefügt."
            }
        } else {
            store.addItem(Item(
                rfidTag: tag.tagId,
                name: Item.defaultName(for: tag.itemData, fallback: "Filament"),
                containerId: container.id,
                itemData: tag.itemData
            ))
            scanMessage = "Filament wurde erstellt und hinzugefügt."
        }
        if let parseError = tag.parseError {
            scanMessage = [scanMessage, parseError].compactMap { $0 }.joined(separator: "\n")
        }
    }
}
