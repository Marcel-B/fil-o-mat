import Foundation

/// Eine Filament-Sorte (Item) mit RFID-Tag. Gehört über `containerId` zu höchstens einem Container.
struct Item: Identifiable, Codable, Hashable {
    var id: String = UUID().uuidString
    var rfidTag: String
    var name: String
    var description: String = ""
    var createdAt: Date = Date()
    var containerId: String?
    var itemData: ItemJsonData?
    /// Filamentgewicht einer vollen Spule in Gramm; wird vom Backend gepflegt.
    /// Das Restgewicht steht pro angebrochener Spule in `Spool`.
    var netWeight: Double?
    /// Gewicht der leeren Spule in Gramm, um eine gewogene Spule umzurechnen.
    var spoolWeight: Double?
    /// Volle Spulen dieser Sorte im Vorrat (bei Tags auf Musterplättchen); `nil`, wenn kein Vorrat geführt wird.
    var spoolsInStock: Int?
    /// Lichtdurchlässigkeit als Transmission Distance (TD, HueForge) in mm, z. B. mit einem TD-1 gemessen.
    /// Steht nur in Fil-o-mat, nicht auf dem Tag.
    var transmissionDistance: Double?
    /// Weitere Tags derselben Sorte, z. B. Musterplättchen an Boxen und Druckern; `nil` bei älteren Daten.
    var extraTags: [String]?

    /// Haupt-Tag und weitere Tags.
    var allTags: [String] {
        ([rfidTag] + (extraTags ?? [])).filter { !$0.isEmpty }
    }

    func hasTag(_ tag: String) -> Bool {
        !tag.isEmpty && allTags.contains(tag)
    }

    /// "3,5 mm", falls die Lichtdurchlässigkeit gemessen ist.
    var transmissionText: String? {
        transmissionDistance.map { "\($0.formatted(.number.precision(.fractionLength(0...2)))) mm" }
    }

    /// Titel im Format "<brand> <type> <variant>", Fallback auf den Namen.
    var displayTitle: String {
        let parts = [itemData?.brand, itemData?.type, itemData?.variant]
            .compactMap { $0?.trimmingCharacters(in: .whitespaces) }
        return parts.isEmpty ? name : parts.joined(separator: " ")
    }

    /// Name für neu gescannte Items, wie in der Android-App: "<brand> <type>".
    static func defaultName(for data: ItemJsonData?, fallback: String) -> String {
        let name = "\(data?.brand ?? "") \(data?.type ?? "")".trimmingCharacters(in: .whitespaces)
        return name.isEmpty ? fallback : name
    }
}

/// Filament-Daten im OpenSpool-Format, wie sie auf dem NFC-Tag stehen.
struct ItemJsonData: Codable, Hashable {
    var protocolName: String?
    var version: String?
    var type: String?
    var colorHex: String?
    var brand: String?
    var minTemp: String?
    var maxTemp: String?
    var bedMinTemp: String?
    var bedMaxTemp: String?
    var variant: String?
    var color: String?

    enum CodingKeys: String, CodingKey {
        case protocolName = "protocol"
        case version
        case type
        case colorHex = "color_hex"
        case brand
        case minTemp = "min_temp"
        case maxTemp = "max_temp"
        case bedMinTemp = "bed_min_temp"
        case bedMaxTemp = "bed_max_temp"
        case variant
        case color
    }

    init(
        protocolName: String? = nil,
        version: String? = nil,
        type: String? = nil,
        colorHex: String? = nil,
        brand: String? = nil,
        minTemp: String? = nil,
        maxTemp: String? = nil,
        bedMinTemp: String? = nil,
        bedMaxTemp: String? = nil,
        variant: String? = nil,
        color: String? = nil
    ) {
        self.protocolName = protocolName
        self.version = version
        self.type = type
        self.colorHex = colorHex
        self.brand = brand
        self.minTemp = minTemp
        self.maxTemp = maxTemp
        self.bedMinTemp = bedMinTemp
        self.bedMaxTemp = bedMaxTemp
        self.variant = variant
        self.color = color
    }

    // Andere OpenSpool-Writer speichern Temperaturen teils als Zahl statt als String,
    // daher werden beide Varianten akzeptiert.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        protocolName = c.lenientString(.protocolName)
        version = c.lenientString(.version)
        type = c.lenientString(.type)
        colorHex = c.lenientString(.colorHex)
        brand = c.lenientString(.brand)
        minTemp = c.lenientString(.minTemp)
        maxTemp = c.lenientString(.maxTemp)
        bedMinTemp = c.lenientString(.bedMinTemp)
        bedMaxTemp = c.lenientString(.bedMaxTemp)
        variant = c.lenientString(.variant)
        color = c.lenientString(.color)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encodeIfPresent(protocolName, forKey: .protocolName)
        try c.encodeIfPresent(version, forKey: .version)
        try c.encodeIfPresent(type, forKey: .type)
        try c.encodeIfPresent(colorHex, forKey: .colorHex)
        try c.encodeIfPresent(brand, forKey: .brand)
        try c.encodeIfPresent(minTemp, forKey: .minTemp)
        try c.encodeIfPresent(maxTemp, forKey: .maxTemp)
        try c.encodeIfPresent(bedMinTemp, forKey: .bedMinTemp)
        try c.encodeIfPresent(bedMaxTemp, forKey: .bedMaxTemp)
        try c.encodeIfPresent(variant, forKey: .variant)
        try c.encodeIfPresent(color, forKey: .color)
    }
}

private extension KeyedDecodingContainer {
    func lenientString(_ key: Key) -> String? {
        if let s = try? decodeIfPresent(String.self, forKey: key) { return s }
        if let i = try? decodeIfPresent(Int.self, forKey: key) { return String(i) }
        if let d = try? decodeIfPresent(Double.self, forKey: key) { return String(d) }
        return nil
    }
}
