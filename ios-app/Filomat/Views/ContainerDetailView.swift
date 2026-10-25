import SwiftUI

struct ContainerDetailView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    var containerId: String

    @State private var showingEdit = false
    @State private var showingDelete = false

    var body: some View {
        if let container = store.container(id: containerId) {
            content(for: container)
        } else {
            ContentUnavailableView("Container nicht gefunden", systemImage: "questionmark.folder")
        }
    }

    private func content(for container: Container) -> some View {
        let printer = store.printer(owningContainer: container.id)
        let spools = store.spools(inContainer: container.id)

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

            // Sorten stehen unter "Filamente"; ein Container kennt nur die Spulen, die darin liegen
            if !spools.isEmpty {
                Section("Spulen (\(spools.count))") {
                    ForEach(spools) { spool in
                        let item = store.item(id: spool.itemId)
                        NavigationLink {
                            SpoolEditView(spool: spool, emptySpoolWeight: item?.spoolWeight, showsItem: true)
                        } label: {
                            LabeledContent {
                                Text(spool.remainingWeight.map { "\(Int($0.rounded())) g" } ?? "Rest unbekannt")
                            } label: {
                                HStack(spacing: 6) {
                                    ColorDot(hex: item?.itemData?.colorHex, size: 12, showsPlaceholder: false)
                                    Text("\(item?.displayTitle ?? "Filament") #\(spool.number)")
                                }
                            }
                        }
                    }
                }
            } else if printer == nil {
                Section("Spulen") {
                    Text("Keine Spulen in diesem Container").foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle(container.name)
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                NavigationLink {
                    PlaceFlowView(target: .container)
                } label: {
                    Label("Einlagern", systemImage: "tray.and.arrow.down")
                }

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
        .alert("Container löschen", isPresented: $showingDelete) {
            Button("Löschen", role: .destructive) {
                store.removeContainer(id: container.id)
                dismiss()
            }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Möchten Sie den Container '\(container.name)' wirklich löschen? Die Spulen darin stehen danach unter 'Lose'.")
        }
    }
}
