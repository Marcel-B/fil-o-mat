import Foundation

/// Im Backend konfigurierter Drucker. Jeder Drucker hat einen eigenen Container für die eingelegten Spulen.
struct Printer: Identifiable, Codable, Hashable {
    var id: String
    var name: String
    var type: PrinterType
    var baseUrl: String
    var hasApiKey: Bool
    var enabled: Bool
    var containerId: String
    /// Anzahl Köpfe bzw. Slots (U1: 4); ältere Backends liefern den Wert nicht.
    var tools: Int?
    var lastSyncAt: String?
    var lastSyncError: String?

    var lastSyncDate: Date? { lastSyncAt.flatMap(BackendDate.parse) }
    var toolCount: Int { max(tools ?? 1, 1) }
}

enum PrinterType: String, Codable, CaseIterable, Identifiable {
    case moonraker = "Moonraker"
    case snapmakerU1 = "SnapmakerU1"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .moonraker: "Klipper / Moonraker"
        case .snapmakerU1: "Snapmaker U1 (paxx12)"
        }
    }

    var addressHint: String {
        switch self {
        case .moonraker: "http://voron.local:7125"
        case .snapmakerU1: "http://192.168.2.155"
        }
    }
}

struct PrinterRequest: Encodable {
    var name: String
    var type: PrinterType
    var baseUrl: String
    var apiKey: String?
    var rfidTag: String?
}

struct SyncResponse: Decodable {
    var recorded: Int
    var error: String?
}

/// Gebuchter Verbrauch eines Drucks.
struct ConsumptionEntry: Identifiable, Decodable, Hashable {
    var id: String
    var printerId: String
    var jobName: String
    var status: String
    var endedAt: String
    var grams: Double
    var tool: Int?
    var itemId: String?

    var endedDate: Date? { BackendDate.parse(endedAt) }
}

/// Datumswerte aus dem Backend (ISO 8601, .NET liefert bis zu 7 Nachkommastellen).
enum BackendDate {
    static func parse(_ text: String) -> Date? {
        var value = text
        if let dot = value.firstIndex(of: "."), let end = value[dot...].firstIndex(where: { !$0.isNumber && $0 != "." }) {
            let fraction = value[value.index(after: dot)..<end].prefix(3)
            value = String(value[..<dot]) + "." + fraction + String(value[end...])
        }
        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return withFraction.date(from: value) ?? ISO8601DateFormatter().date(from: value)
    }
}
