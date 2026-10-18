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

struct ContainerStatusCard: View {
    var container: Container

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "checkmark.circle.fill").foregroundStyle(.tint).font(.title2)
            VStack(alignment: .leading, spacing: 2) {
                Text("Container erkannt").font(.subheadline.weight(.semibold))
                Text(container.name)
                if !container.description.isEmpty {
                    Text(container.description).font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
        .padding()
        .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))
    }
}

struct ItemStatusCard: View {
    var item: Item

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "checkmark.circle.fill").foregroundStyle(.tint).font(.title2)
            ColorDot(hex: item.itemData?.colorHex, size: 40, showsPlaceholder: false)
            VStack(alignment: .leading, spacing: 2) {
                Text("Filament erkannt").font(.subheadline.weight(.semibold))
                Text(item.displayTitle)
                HStack(spacing: 8) {
                    Text("Farbe:").foregroundStyle(.secondary)
                    ColorInfo(data: item.itemData)
                }
                .font(.caption)
            }
            Spacer()
        }
        .padding()
        .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))
    }
}
