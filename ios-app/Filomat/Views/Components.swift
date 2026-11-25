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
    var label: LocalizedStringKey
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

/// Knopf, der einen Tag scannt und das Ergebnis weiterreicht. Abbrechen im Scanfenster ist kein Fehler.
/// Ohne NFC (iPad, Mac) wählt man stattdessen aus der Liste, falls `pick` gesetzt ist.
struct ScanButton: View {
    var title: LocalizedStringKey
    var prompt: String.LocalizationValue
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
            onTag(try await NFCService.shared.scan(prompt: String(localized: prompt)))
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

    var buttonTitle: LocalizedStringKey {
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
                            ItemHeader(item: item, detail: nil)
                                .foregroundStyle(.primary)
                        }
                    }
                case .container:
                    ForEach(containers) { container in
                        Button {
                            pick(ScannedTag(tagId: container.rfidTag))
                        } label: {
                            LabeledContent(container.name, value: "\(store.spools(inContainer: container.id).count)")
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
            .filter { matches($0.displayTitle) }
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
    var title: LocalizedStringKey
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
