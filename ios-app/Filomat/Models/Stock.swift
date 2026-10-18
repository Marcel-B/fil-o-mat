import Foundation

/// Vorrat einer Sorte: volle Spulen und angebrochene Spulen mit Restgewicht.
struct StockEntry: Identifiable, Hashable {
    var item: Item
    var openedSpools: [Spool]

    var id: String { item.id }

    var fullSpools: Int { item.spoolsInStock ?? 0 }

    /// Restgewicht der angebrochenen Spulen, soweit bekannt.
    var openedGrams: Double {
        openedSpools.compactMap(\.remainingWeight).reduce(0, +)
    }

    /// Filament insgesamt in Gramm; `nil`, wenn weder Füllgewicht noch Restgewichte bekannt sind.
    var totalGrams: Double? {
        let knowsOpened = openedSpools.contains { $0.remainingWeight != nil }
        guard let net = item.netWeight else { return knowsOpened ? openedGrams : nil }
        return Double(fullSpools) * net + openedGrams
    }

    /// Sollte nachgekauft werden: es wird Vorrat geführt, keine volle Spule ist mehr da und
    /// die angebrochenen haben zusammen weniger als `threshold` Gramm (oder es gibt keine).
    func isLow(threshold: Double = Stock.lowThreshold) -> Bool {
        guard item.spoolsInStock != nil, fullSpools == 0 else { return false }
        if openedSpools.isEmpty { return true }
        // Bei unbekanntem Rest lieber nicht warnen
        guard openedSpools.allSatisfy({ $0.remainingWeight != nil }) else { return false }
        return openedGrams < threshold
    }
}

enum Stock {
    /// Unter so viel Gramm gilt eine Sorte ohne volle Spulen als knapp.
    static let lowThreshold: Double = 250

    /// Vorrat aller Sorten, nach Titel sortiert.
    static func entries(items: [Item], spools: [Spool]) -> [StockEntry] {
        let byItem = Dictionary(grouping: spools, by: \.itemId)
        return items
            .map { StockEntry(item: $0, openedSpools: (byItem[$0.id] ?? []).sorted { $0.number < $1.number }) }
            .sorted { $0.item.displayTitle.localizedStandardCompare($1.item.displayTitle) == .orderedAscending }
    }
}
