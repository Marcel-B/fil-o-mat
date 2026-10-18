import Foundation

/// Was ein Scan über den Tag selbst weiß, unabhängig vom Inhalt.
struct TagDetails: Equatable {
    /// UID als Hex-String in Großbuchstaben.
    var uid: String
    var technology: String
    /// "beschreibbar", "schreibgeschützt" oder "kein NDEF".
    var ndefStatus: String?
    /// Speicher für NDEF in Bytes.
    var capacity: Int?
    var records: [TagRecord] = []
}

/// Ein NDEF-Record, so wie er auf dem Tag steht.
struct TagRecord: Equatable {
    /// Type Name Format als Rohwert (1 = Well-known, 2 = Media, …).
    var format: UInt8
    var type: String
    var payload: Data

    var formatName: String {
        switch format {
        case 0: return "Leer"
        case 1: return "Well-known"
        case 2: return "Media"
        case 3: return "URI"
        case 4: return "Extern"
        case 5: return "Unbekannt"
        case 6: return "Unverändert"
        default: return "TNF \(format)"
        }
    }

    var isJSON: Bool { format == 2 && type == "application/json" }

    /// Inhalt als lesbarer Text: Text- und URI-Records dekodiert, sonst UTF-8, sonst Hex.
    var payloadText: String {
        if format == 1, type == "T", let status = payload.first {
            let languageLength = Int(status & 0x3F)
            let text = payload.dropFirst(1 + languageLength)
            let encoding: String.Encoding = status & 0x80 == 0 ? .utf8 : .utf16
            if let decoded = String(data: Data(text), encoding: encoding) { return decoded }
        }
        if format == 1, type == "U", let code = payload.first {
            let prefixes = ["", "http://www.", "https://www.", "http://", "https://"]
            let prefix = Int(code) < prefixes.count ? prefixes[Int(code)] : ""
            if let rest = String(data: Data(payload.dropFirst()), encoding: .utf8) { return prefix + rest }
        }
        if let text = String(data: payload, encoding: .utf8) { return text }
        let hex = payload.prefix(64).map { String(format: "%02X", $0) }.joined(separator: " ")
        return payload.count > 64 ? hex + " …" : hex
    }
}

/// Ein Feld des OpenSpool-JSON mit deutscher Bezeichnung.
struct TagField: Equatable, Identifiable {
    var key: String
    var label: String
    var value: String
    var id: String { key }
}

enum TagInfo {
    /// Bekannte OpenSpool-Felder in Anzeigereihenfolge. `subtype` schreibt der Snapmaker U1,
    /// wo die Apps `variant` schreiben.
    private static let knownFields: [(key: String, label: String)] = [
        ("protocol", "Protokoll"),
        ("version", "Version"),
        ("brand", "Marke"),
        ("type", "Typ"),
        ("variant", "Variante"),
        ("subtype", "Untertyp"),
        ("color", "Farbname"),
        ("color_name", "Farbname"),
        ("color_hex", "Farbe"),
        ("additional_color_hexes", "Weitere Farben"),
        ("alpha", "Transparenz"),
        ("min_temp", "Düse min."),
        ("max_temp", "Düse max."),
        ("bed_min_temp", "Bett min."),
        ("bed_max_temp", "Bett max."),
        ("weight", "Gewicht"),
        ("diameter", "Durchmesser"),
    ]

    /// Alle Felder des JSON: bekannte in fester Reihenfolge, unbekannte danach alphabetisch,
    /// damit nichts verloren geht, was ein anderer Writer auf den Tag geschrieben hat.
    static func fields(fromJSON data: Data) -> [TagField]? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        var fields: [TagField] = []
        for (key, label) in knownFields {
            if let value = object[key] { fields.append(TagField(key: key, label: label, value: text(value))) }
        }
        let known = Set(knownFields.map(\.key))
        for key in object.keys.filter({ !known.contains($0) }).sorted() {
            fields.append(TagField(key: key, label: key, value: text(object[key]!)))
        }
        return fields
    }

    /// Das JSON eingerückt und mit sortierten Schlüsseln, zum Nachlesen.
    static func prettyJSON(_ data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data),
              let pretty = try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys]) else {
            return nil
        }
        return String(data: pretty, encoding: .utf8)
    }

    private static func text(_ value: Any) -> String {
        switch value {
        case let string as String: return string
        case let number as NSNumber:
            // JSONSerialization liefert Bools als NSNumber; nur echte Bools als Ja/Nein zeigen.
            if CFGetTypeID(number) == CFBooleanGetTypeID() { return number.boolValue ? "ja" : "nein" }
            return number.stringValue
        case let array as [Any]: return array.map(text).joined(separator: ", ")
        case is NSNull: return "–"
        default:
            if let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]),
               let string = String(data: data, encoding: .utf8) {
                return string
            }
            return "\(value)"
        }
    }
}
