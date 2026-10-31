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
struct ScanButton: View {
    var title: LocalizedStringKey
    var prompt: String.LocalizationValue
    var onTag: (ScannedTag) -> Void
    var onError: (String) -> Void = { _ in }

    @State private var isScanning = false

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
        } else {
            Text("NFC ist auf diesem Gerät nicht verfügbar.").foregroundStyle(.red)
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
