import Foundation

/// Welche Spulen die Spulenliste zeigt (wie die Auswahl in der Web-Oberfläche).
enum SpoolFilter: String, CaseIterable, Identifiable {
    case all, printer, storage

    var id: String { rawValue }

    var title: String {
        switch self {
        case .all: return String(localized: "Alle")
        case .printer: return String(localized: "Im Drucker")
        case .storage: return String(localized: "Im Lager")
        }
    }
}

/// Eine Zeile der Spulenliste: die Spule mit ihrer Sorte und ihrem Platz.
struct SpoolListEntry: Identifiable {
    var spool: Spool
    var item: Item?
    var title: String
    var place: String
    /// Restgewicht; ohne gewogenen Wert rechnet das Backend mit einer vollen Spule der Sorte.
    var remaining: Double?
    /// Anteil des Füllgewichts einer neuen Spule (0...1), wenn beides bekannt ist.
    var fraction: Double?

    var id: String { spool.id }
    var isWeighed: Bool { spool.remainingWeight != nil }

    /// Spulen gefiltert und durchsucht (Sorte, Farbe, Platz, "#3"), sortiert nach Sorte und Nummer.
    static func entries(spools: [Spool], items: [Item], printers: [Printer], containers: [Container],
                        query: String = "", filter: SpoolFilter = .all) -> [SpoolListEntry] {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        return spools
            .filter { spool in
                switch filter {
                case .all: return true
                case .printer: return spool.printerId != nil
                case .storage: return spool.printerId == nil
                }
            }
            .map { spool -> SpoolListEntry in
                let item = items.first { $0.id == spool.itemId }
                let remaining = spool.remainingWeight ?? item?.netWeight
                var fraction: Double?
                if let remaining, let net = item?.netWeight, net > 0 { fraction = min(1, remaining / net) }
                return SpoolListEntry(spool: spool, item: item, title: item?.displayTitle ?? String(localized: "Filament"),
                                      place: spool.locationText(printers: printers, containers: containers),
                                      remaining: remaining, fraction: fraction)
            }
            .filter { entry in
                q.isEmpty || ([entry.title, entry.place, entry.item?.itemData?.color, "#\(entry.spool.number)"] as [String?])
                    .contains { $0?.lowercased().contains(q) == true }
            }
            .sorted {
                let order = $0.title.localizedStandardCompare($1.title)
                return order == .orderedSame ? $0.spool.number < $1.spool.number : order == .orderedAscending
            }
    }
}
