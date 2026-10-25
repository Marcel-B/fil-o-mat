import SwiftUI

/// Farbkreis für einen `color_hex`-Wert; ohne gültige Farbe ein grauer Platzhalter mit "n/a".
struct ColorDot: View {
    var hex: String?
    var size: CGFloat = 40
    var showsPlaceholder = true

    var body: some View {
        if let color = HexColor(hex) {
            Circle()
                .fill(color.color)
                .frame(width: size, height: size)
                .overlay(Circle().strokeBorder(Color.secondary.opacity(0.5), lineWidth: size >= 30 ? 2 : 1))
        } else if showsPlaceholder {
            Circle()
                .fill(Color(uiColor: .secondarySystemFill))
                .frame(width: size, height: size)
                .overlay(Text("n/a").font(.caption2).foregroundStyle(.secondary))
        }
    }
}

struct InfoRow: View {
    var label: String
    var value: String

    var body: some View {
        LabeledContent(label, value: value)
    }
}

/// Farbe als Kreis plus Bezeichnung, oder "n/a".
struct ColorInfo: View {
    var data: ItemJsonData?
    var dotSize: CGFloat = 16

    var body: some View {
        HStack(spacing: 8) {
            if let label = data?.colorLabel {
                ColorDot(hex: data?.colorHex, size: dotSize, showsPlaceholder: false)
                Text(label)
            } else {
                Text("n/a").foregroundStyle(.secondary)
            }
        }
    }
}

/// Filament-Zeile in der Container-Ansicht mit allen Tag-Daten.
struct ItemDetailRow: View {
    var item: Item
    var isLooseContainer: Bool
    var onRemove: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.displayTitle).font(.headline)
                    Text("RFID: \(item.rfidTag)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button(role: .destructive, action: onRemove) {
                    Image(systemName: "minus.circle.fill")
                        .font(.title2)
                        .foregroundStyle(.red)
                }
                .buttonStyle(.borderless)
                .accessibilityLabel(isLooseContainer ? "Löschen" : "Entfernen")
            }
            if !item.description.isEmpty {
                Text(item.description).font(.caption).foregroundStyle(.secondary)
            }
            if let data = item.itemData {
                Divider()
                Text("Tag-Daten:").font(.caption).foregroundStyle(.tint)
                if let brand = data.brand { InfoRow(label: "Marke", value: brand) }
                if let type = data.type { InfoRow(label: "Typ", value: type) }
                LabeledContent("Farbe") { ColorInfo(data: data, dotSize: 20) }
                if let min = data.minTemp, let max = data.maxTemp {
                    InfoRow(label: "Temperatur", value: "\(min)°C - \(max)°C")
                }
                if let min = data.bedMinTemp, let max = data.bedMaxTemp {
                    InfoRow(label: "Bett-Temp", value: "\(min)°C - \(max)°C")
                }
            }
        }
        .font(.subheadline)
        .padding(.vertical, 4)
    }
}

/// Knopf, der einen Tag scannt und das Ergebnis weiterreicht. Abbrechen im Scanfenster ist kein Fehler.
/// Ohne NFC (iPad, Mac) wählt man stattdessen aus der Liste, falls `pick` gesetzt ist.
struct ScanButton: View {
    var title: String
    var prompt: String
    var pick: TagPick?
    var onTag: (ScannedTag) -> Void
    var onError: (String) -> Void = { _ in }

    @State private var isScanning = false
    @State private var showingPicker = false

    var body: some View {
        if NFCService.isAvailable {
            Button {
                Task { await scan() }
            } label: {
                HStack {
                    if isScanning { ProgressView() } else { Image(systemName: "wave.3.right") }
                    Text(isScanning ? "Scanne..." : title)
                }
                .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(isScanning)
        } else if let pick {
            Button {
                showingPicker = true
            } label: {
                Label(pick.buttonTitle, systemImage: "list.bullet")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .sheet(isPresented: $showingPicker) {
                TagPickerView(kind: pick, onPick: onTag)
            }
        }
    }

    private func scan() async {
        isScanning = true
        defer { isScanning = false }
        do {
            onTag(try await NFCService.shared.scan(prompt: prompt))
        } catch NFCError.cancelled {
            // Nutzer hat das Scanfenster geschlossen
        } catch {
            onError(error.localizedDescription)
        }
    }
}

/// Was auf Geräten ohne NFC statt eines Scans aus der Liste gewählt wird.
enum TagPick {
    case item
    case container

    var buttonTitle: String {
        switch self {
        case .item: return "Filament wählen"
        case .container: return "Container wählen"
        }
    }
}

/// Ersatz für einen Scan ohne NFC: Sorte oder Container aus der Liste wählen. Liefert dasselbe wie ein Scan
/// des zugehörigen Tags, damit die Abläufe unverändert bleiben.
struct TagPickerView: View {
    var kind: TagPick
    var excludedItemIds: Set<String> = []
    var onPick: (ScannedTag) -> Void

    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""

    var body: some View {
        NavigationStack {
            List {
                switch kind {
                case .item:
                    ForEach(items) { item in
                        Button {
                            pick(ScannedTag(tagId: item.rfidTag, itemData: item.itemData))
                        } label: {
                            ItemHeader(item: item, detail: item.containerId.flatMap { store.container(id: $0)?.name })
                                .foregroundStyle(.primary)
                        }
                    }
                case .container:
                    ForEach(containers) { container in
                        Button {
                            pick(ScannedTag(tagId: container.rfidTag))
                        } label: {
                            LabeledContent(container.name, value: "\(store.filamentCount(in: container.id))")
                                .foregroundStyle(.primary)
                        }
                    }
                }
            }
            .overlay {
                if isEmpty {
                    ContentUnavailableView.search(text: search)
                }
            }
            .searchable(text: $search)
            .navigationTitle(kind.buttonTitle)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
        }
    }

    private var items: [Item] {
        store.items
            .filter { !excludedItemIds.contains($0.id) && matches($0.displayTitle) }
            .sorted { $0.displayTitle.localizedStandardCompare($1.displayTitle) == .orderedAscending }
    }

    private var containers: [Container] {
        store.containers.filter { matches($0.name) }
    }

    private var isEmpty: Bool {
        kind == .item ? items.isEmpty : containers.isEmpty
    }

    private func matches(_ text: String) -> Bool {
        search.isEmpty || text.localizedStandardContains(search)
    }

    private func pick(_ tag: ScannedTag) {
        dismiss()
        onPick(tag)
    }
}

/// Kopfzeile einer Sorte: Farbe, Titel und eine Zeile darunter.
struct ItemHeader: View {
    var item: Item
    var detail: String?

    var body: some View {
        HStack(spacing: 12) {
            ColorDot(hex: item.itemData?.colorHex, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(item.displayTitle).font(.headline)
                if let detail {
                    Text(detail).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

/// Auswahl eines Spulenplatzes: Lager, ein Container im Lager oder ein Kopf eines Druckers.
struct SpoolPlacePicker: View {
    @Environment(ContainerStore.self) private var store
    var title: String
    @Binding var selection: SpoolPlace

    var body: some View {
        Picker(title, selection: $selection) {
            Text("Lager").tag(SpoolPlace.storage)
            ForEach(store.storageContainers) { container in
                Text(container.name).tag(SpoolPlace.container(container.id))
            }
            ForEach(store.printers) { printer in
                ForEach(0..<printer.toolCount, id: \.self) { tool in
                    Text(printer.placeName(tool: tool)).tag(SpoolPlace.printer(printer.id, tool: tool))
                }
            }
        }
    }
}
