import XCTest
@testable import Filomat

final class StockTests: XCTestCase {
    private func spool(_ number: Int, _ grams: Double?) -> Spool {
        Spool(id: "s\(number)", itemId: "pla", number: number, remainingWeight: grams)
    }

    func testTotalsAndLowStock() {
        let item = Item(id: "pla", rfidTag: "PLA", name: "PLA", netWeight: 1000, spoolsInStock: 2)
        var entry = StockEntry(item: item, openedSpools: [spool(1, 430), spool(2, 120)])
        XCTAssertEqual(entry.totalGrams, 2550)
        XCTAssertFalse(entry.isLow())

        entry.item.spoolsInStock = 0
        XCTAssertEqual(entry.totalGrams, 550)
        XCTAssertFalse(entry.isLow())

        entry.openedSpools = [spool(2, 120)]
        XCTAssertTrue(entry.isLow())

        // Unbekannter Rest: lieber keine Warnung
        entry.openedSpools = [spool(3, nil)]
        XCTAssertFalse(entry.isLow())

        entry.openedSpools = []
        XCTAssertTrue(entry.isLow())
    }

    func testUntrackedStockIsNeverLow() {
        let entry = StockEntry(item: Item(rfidTag: "A", name: "A"), openedSpools: [])
        XCTAssertFalse(entry.isLow())
        XCTAssertNil(entry.totalGrams)
    }

    func testEntriesGroupSpoolsBySort() {
        let items = [Item(id: "b", rfidTag: "B", name: "Zeta"), Item(id: "pla", rfidTag: "PLA", name: "Alpha")]
        let entries = Stock.entries(items: items, spools: [spool(2, 10), spool(1, 20)])
        XCTAssertEqual(entries.map(\.item.name), ["Alpha", "Zeta"])
        XCTAssertEqual(entries[0].openedSpools.map(\.number), [1, 2])
        XCTAssertTrue(entries[1].openedSpools.isEmpty)
    }
}
