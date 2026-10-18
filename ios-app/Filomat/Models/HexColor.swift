import SwiftUI

/// Farbe aus einem `color_hex`-Wert ("FF0000", "#FF0000" oder "#AARRGGBB" wie bei Android).
struct HexColor: Equatable {
    var red: Double
    var green: Double
    var blue: Double
    var alpha: Double

    /// Normalisierte Darstellung mit führendem "#", oder `nil` bei leerer Eingabe.
    static func display(_ raw: String?) -> String? {
        guard let trimmed = raw?.trimmingCharacters(in: .whitespaces), !trimmed.isEmpty else { return nil }
        return trimmed.hasPrefix("#") ? trimmed : "#\(trimmed)"
    }

    /// Hex-Wert ohne "#", so wie er auf den Tag geschrieben wird; `nil` bei leerer Eingabe.
    static func stored(_ raw: String) -> String? {
        let trimmed = raw.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return nil }
        return trimmed.hasPrefix("#") ? String(trimmed.dropFirst()) : trimmed
    }

    init?(_ raw: String?) {
        guard let display = Self.display(raw) else { return nil }
        let hex = String(display.dropFirst())
        guard hex.count == 6 || hex.count == 8, hex.allSatisfy(\.isHexDigit), let value = UInt32(hex, radix: 16) else { return nil }
        if hex.count == 8 {
            alpha = Double((value >> 24) & 0xFF) / 255
        } else {
            alpha = 1
        }
        red = Double((value >> 16) & 0xFF) / 255
        green = Double((value >> 8) & 0xFF) / 255
        blue = Double(value & 0xFF) / 255
    }

    var color: Color {
        Color(.sRGB, red: red, green: green, blue: blue, opacity: alpha)
    }
}

extension ItemJsonData {
    /// Anzeigename der Farbe: Klartext, sonst der Hex-Wert.
    var colorLabel: String? {
        if let color = color?.trimmingCharacters(in: .whitespaces), !color.isEmpty {
            return color
        }
        return HexColor.display(colorHex)
    }
}
