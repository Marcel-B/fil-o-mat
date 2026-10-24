import Foundation

/// Zustand eines Druckers mit den Spulen der App und Hinweisen (`/api/printers/status`).
struct PrinterLive: Identifiable, Decodable, Hashable {
    var printerId: String
    var name: String
    var type: PrinterType
    var updatedAt: String?
    var status: PrinterStatus
    var tools: [ToolLive]
    var hints: [PrinterHint]

    var id: String { printerId }
    var updatedDate: Date? { updatedAt.flatMap(BackendDate.parse) }
    var warnings: [PrinterHint] { hints.filter(\.warning) }
}

struct PrinterStatus: Decodable, Hashable {
    /// `Idle`, `Printing`, `Paused`, `Finished`, `Cancelled`, `Error`, `Offline`
    var state: String
    var jobName: String?
    var jobKey: String?
    /// 0–100
    var progress: Double?
    var remainingSeconds: Int?
    var elapsedSeconds: Int?
    var layer: Int?
    var totalLayers: Int?
    var activeTool: Int?
    var heaters: [Heater]?
    var slots: [SlotReport]?
    var dryers: [DryerReport]?
    var jobGramsPerTool: [Double]?
    var jobGramsEstimated: Bool?
    var message: String?
    var hasThumbnail: Bool?

    var isActive: Bool { state == "Printing" || state == "Paused" }
    var hasJob: Bool { isActive || state == "Finished" || state == "Cancelled" }

    var title: String {
        switch state {
        case "Printing": "Druckt"
        case "Paused": "Pausiert"
        case "Finished": "Fertig"
        case "Cancelled": "Abgebrochen"
        case "Error": "Fehler"
        case "Offline": "Nicht erreichbar"
        default: "Bereit"
        }
    }

    var symbol: String {
        switch state {
        case "Printing": "printer.fill"
        case "Paused": "pause.circle"
        case "Finished": "checkmark.circle"
        case "Cancelled": "xmark.circle"
        case "Error": "exclamationmark.triangle"
        case "Offline": "wifi.slash"
        default: "moon.zzz"
        }
    }

    /// "noch 1:23 h" bzw. "noch 12 min"
    var remainingText: String? {
        guard isActive, let seconds = remainingSeconds, seconds > 0 else { return nil }
        return "noch " + PrinterStatus.duration(seconds)
    }

    static func duration(_ seconds: Int) -> String {
        seconds >= 3600
            ? String(format: "%d:%02d h", seconds / 3600, seconds % 3600 / 60)
            : "\(max(seconds / 60, 1)) min"
    }

    /// "Druckt benchy · 45 % · noch 32 min"
    var summary: String {
        var parts = [title]
        if hasJob, let jobName, !jobName.isEmpty { parts[0] += " \(jobName)" }
        if isActive, let progress { parts.append("\(Int(progress)) %") }
        if let remainingText { parts.append(remainingText) }
        return parts.joined(separator: " · ")
    }
}

struct Heater: Decodable, Hashable {
    var name: String
    var tool: Int?
    var actual: Double?
    var target: Double?

    var text: String {
        let actual = actual.map { "\(Int($0.rounded())) °C" } ?? "–"
        guard let target, target > 0 else { return actual }
        return "\(actual) / \(Int(target.rounded())) °C"
    }
}

struct SlotReport: Decodable, Hashable {
    var tool: Int
    var loaded: Bool?
    var type: String?
    var colorHex: String?
    var remainPercent: Double?
    var tagUid: String?
}

struct DryerReport: Decodable, Hashable {
    var name: String
    var humidityPercent: Double?
    /// Bambu: 1 (feucht) bis 5 (trocken)
    var humidityLevel: Int?
    var temperature: Double?
    var tools: [Int]

    var text: String {
        var parts: [String] = []
        if let humidityPercent { parts.append("\(Int(humidityPercent.rounded())) % Feuchte") }
        else if let humidityLevel { parts.append("Feuchtestufe \(humidityLevel)/5") }
        if let temperature { parts.append("\(Int(temperature.rounded())) °C") }
        return parts.joined(separator: " · ")
    }
}

struct ToolLive: Decodable, Hashable, Identifiable {
    var tool: Int
    var place: String
    var spoolId: String?
    var spoolNumber: Int?
    var itemId: String?
    var itemName: String?
    var type: String?
    var colorHex: String?
    var remainingGrams: Double?
    var reported: SlotReport?
    var neededGrams: Double?
    var enough: Bool?

    var id: Int { tool }

    /// "PLA Schwarz #2", was der Drucker meldet, oder "leer".
    var title: String {
        if let itemName { return spoolNumber.map { "\(itemName) #\($0)" } ?? itemName }
        if let type = reported?.type, reported?.loaded != false { return type }
        return "leer"
    }
}

struct PrinterHint: Decodable, Hashable, Identifiable {
    /// `SpoolShort`, `TagMismatch`, `SlotMismatch`, `SlotEmpty`, `SlotUnknown`, `Temperature`, `Humidity`
    var kind: String
    var warning: Bool
    var tool: Int?
    var text: String

    var id: String { "\(kind)-\(tool ?? -1)-\(text)" }

    var symbol: String {
        switch kind {
        case "SpoolShort": "scalemass"
        case "TagMismatch", "SlotMismatch", "SlotEmpty", "SlotUnknown": "tag"
        case "Temperature": "thermometer.medium"
        case "Humidity": "humidity"
        default: "exclamationmark.triangle"
        }
    }
}

// MARK: - Statistik

struct StatsTotals: Decodable, Hashable {
    var grams: Double
    var jobs: Int
    var completed: Int
    var cancelled: Int
    var printSeconds: Int
    var cancelledGrams: Double
}

struct StatsResponse: Decodable {
    struct Month: Decodable, Hashable, Identifiable {
        var month: String
        var grams: Double
        var jobs: Int
        var printSeconds: Int
        var id: String { month }

        /// "Okt." aus "2026-10"
        var label: String {
            let parts = month.split(separator: "-")
            guard parts.count == 2, let number = Int(parts[1]), (1...12).contains(number) else { return month }
            return Calendar.current.shortMonthSymbols[number - 1]
        }

        /// "10/26" aus "2026-10", eindeutig über mehrere Jahre
        var yearLabel: String {
            let parts = month.split(separator: "-")
            guard parts.count == 2 else { return month }
            return "\(parts[1])/\(parts[0].suffix(2))"
        }
    }

    struct PrinterEntry: Decodable, Hashable, Identifiable {
        var printerId: String
        var name: String
        var totals: StatsTotals
        var id: String { printerId }
    }

    struct ItemEntry: Decodable, Hashable, Identifiable {
        var itemId: String?
        var name: String
        var type: String?
        var colorHex: String?
        var grams: Double
        var jobs: Int
        var id: String { itemId ?? "ohne" }
    }

    struct TypeEntry: Decodable, Hashable, Identifiable {
        var type: String
        var grams: Double
        var id: String { type }
    }

    var from: String
    var totals: StatsTotals
    var months: [Month]
    var printers: [PrinterEntry]
    var items: [ItemEntry]
    var types: [TypeEntry]
}

// MARK: - Meldungen

struct NotificationSettings: Decodable, Equatable {
    var url: String?
    var hasToken: Bool
    /// `false`, wenn die Adresse aus der Server-Konfiguration kommt
    var fromApp: Bool
    var configured: Bool
}

/// `token` `nil` lässt den gespeicherten Token, ein leerer Text löscht ihn.
struct NotificationSettingsRequest: Encodable {
    var url: String?
    var token: String?
}

extension Double {
    /// "1,2 kg" ab 1000 g, sonst "340 g"
    var gramsText: String {
        self >= 1000
            ? (self / 1000).formatted(.number.precision(.fractionLength(1))) + " kg"
            : "\(Int(rounded())) g"
    }
}
