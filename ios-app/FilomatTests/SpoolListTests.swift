import XCTest
@testable import Filomat

final class SpoolListTests: XCTestCase {
    private let pla = Item(id: "pla", rfidTag: "A", name: "PLA", itemData: ItemJsonData(type: "PLA", brand: "Prusament", color: "Galaxy Black"),
                           netWeight: 1000)
    private let petg = Item(id: "petg", rfidTag: "B", name: "PETG", itemData: ItemJsonData(type: "PETG", brand: "Sunlu"), netWeight: 1000)
    private let printer = Printer(id: "u1", name: "U1", type: .snapmakerU1, baseUrl: "", hasApiKey: false, enabled: true,
                                  containerId: "c-u1", tools: 4)
    private let box = Container(id: "box", rfidTag: "X", name: "Regal oben")

    private var spools: [Spool] {
        [Spool(id: "s2", itemId: "pla", number: 2, remainingWeight: 50, printerId: "u1", tool: 1),
         Spool(id: "s1", itemId: "pla", number: 1, remainingWeight: nil, containerId: "box"),
         Spool(id: "s3", itemId: "petg", number: 1, remainingWeight: 800)]
    }

    private func entries(_ query: String = "", _ filter: SpoolFilter = .all) -> [SpoolListEntry] {
        SpoolListEntry.entries(spools: spools, items: [pla, petg], printers: [printer], containers: [box], query: query, filter: filter)
    }

    func testSortsBySortThenNumberAndNamesThePlace() {
        let all = entries()
        XCTAssertEqual(all.map(\.spool.id), ["s1", "s2", "s3"])
        XCTAssertEqual(all.map(\.place), ["Regal oben", "U1 Kopf 2", "Lager"])
    }

    func testUnweighedSpoolCountsAsFull() {
        let first = entries()[0]
        XCTAssertFalse(first.isWeighed)
        XCTAssertEqual(first.remaining, 1000)
        XCTAssertEqual(entries()[1].fraction ?? 0, 0.05, accuracy: 0.0001)
    }

    func testFiltersByPrinterOrStorage() {
        XCTAssertEqual(entries("", .printer).map(\.spool.id), ["s2"])
        XCTAssertEqual(entries("", .storage).map(\.spool.id), ["s1", "s3"])
    }

    func testSearchesSortColorPlaceAndNumber() {
        XCTAssertEqual(entries("galaxy").map(\.spool.id), ["s1", "s2"])
        XCTAssertEqual(entries("regal").map(\.spool.id), ["s1"])
        XCTAssertEqual(entries("sunlu #1").map(\.spool.id), [])
        XCTAssertEqual(entries("#2").map(\.spool.id), ["s2"])
    }
}
