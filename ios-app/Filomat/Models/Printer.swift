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

    /// "U1 Kopf 2", "Core One Slot 3" oder nur der Name bei einem Kopf.
    func placeName(tool: Int?) -> String {
        guard toolCount > 1, let tool else { return name }
        return type == .prusaLink || type == .anycubic || type == .bambu
            ? String(localized: "\(name) Slot \(tool + 1)")
            : String(localized: "\(name) Kopf \(tool + 1)")
    }
}

enum PrinterType: String, Codable, CaseIterable, Identifiable {
    case moonraker = "Moonraker"
    case snapmakerU1 = "SnapmakerU1"
    case prusaLink = "PrusaLink"
    case anycubic = "Anycubic"
    case bambu = "Bambu"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .moonraker: "Klipper / Moonraker"
        case .snapmakerU1: "Snapmaker U1 (paxx12)"
        case .prusaLink: "Prusa (PrusaLink)"
        case .anycubic: String(localized: "Anycubic Kobra (LAN-Modus)")
        case .bambu: "Bambu Lab P1S, X1, A1 (LAN)"
        }
    }

    var addressHint: String {
        switch self {
        case .moonraker: "http://voron.local:7125"
        case .snapmakerU1: "http://192.168.2.155"
        case .prusaLink: "http://prusa-core-one.local"
        case .anycubic: "http://192.168.2.160"
        case .bambu: "http://192.168.2.170"
        }
    }

    /// Bambu verlangt den Zugangscode, die anderen einen optionalen API-Schlüssel.
    var keyLabel: String {
        self == .bambu ? String(localized: "Zugangscode") : String(localized: "API-Schlüssel (optional)")
    }

    var requiresKey: Bool { self == .bambu }

    /// Erklärung unter dem Formular beim Anlegen.
    var setupHint: String {
        switch self {
        case .moonraker:
            String(localized: "Der Verbrauch wird von der Spule abgezogen, die in der App an diesem Drucker eingelegt ist.")
        case .snapmakerU1:
            String(localized: "Der U1 erkennt die Spulen in jedem Kopf über ihre Tags. Der Verbrauch wird pro Kopf abgezogen.")
        case .prusaLink:
            String(localized: "Den API-Schlüssel zeigt der Drucker unter Einstellungen > Netzwerk > PrusaLink. Mit MMU3 wird pro Slot abgezogen, von der Spule, die in der App an diesem Slot eingelegt ist.")
        case .anycubic:
            String(localized: "Am Drucker Einstellungen > Netzwerk > LAN-Modus einschalten (die Anycubic-Cloud ist dann aus). Mit ACE wird pro Slot abgezogen, von der Spule, die in der App an diesem Slot eingelegt ist.")
        case .bambu:
            String(localized: "Den Zugangscode zeigt der Drucker unter Einstellungen > WLAN, die Seriennummer liest das Backend selbst. Abgezogen wird das Slicer-Gewicht aus der Druckdatei, mit AMS pro Slot (Slot 1–4 AMS, Slot 5 externe Spule), von der Spule, die in der App an diesem Slot eingelegt ist.")
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

/// Änderungen an einem Drucker. `apiKey` `nil` lässt den Schlüssel, ein leerer Text löscht ihn.
struct PrinterUpdateRequest: Encodable {
    var name: String
    var baseUrl: String
    var apiKey: String?
    var enabled: Bool
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
