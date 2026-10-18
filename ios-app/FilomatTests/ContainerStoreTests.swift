import XCTest
@testable import Filomat

@MainActor
final class ContainerStoreTests: XCTestCase {
    // XCTest erzeugt pro Testmethode eine eigene Instanz, daher ist die Datei je Test eindeutig.
    private let fileURL = FileManager.default.temporaryDirectory
        .appendingPathComponent(UUID().uuidString)
        .appendingPathComponent("store.json")

    func testCreatesStandardContainersOnce() {
        let store = ContainerStore(fileURL: fileURL)
        XCTAssertEqual(store.containers.map(\.id), [Container.printerId, Container.looseId])

        let reloaded = ContainerStore(fileURL: fileURL)
        XCTAssertEqual(reloaded.containers.count, 2)
    }

    func testPersistsContainersAndItems() {
        let store = ContainerStore(fileURL: fileURL)
        let box = Container(rfidTag: "AA", name: "Box 1", capacity: 4)
        store.addContainer(box)
        let data = ItemJsonData(type: "PLA", colorHex: "FF0000", brand: "Prusament")
        store.addItem(Item(rfidTag: "BB", name: "Prusament PLA", containerId: box.id, itemData: data))

        let reloaded = ContainerStore(fileURL: fileURL)
        XCTAssertEqual(reloaded.findContainer(byTag: "AA")?.capacity, 4)
        XCTAssertEqual(reloaded.items(in: box.id).first?.itemData, data)
    }

    func testDoesNotAddItemWithDuplicateTag() {
        let store = ContainerStore(fileURL: fileURL)
        store.addItem(Item(rfidTag: "BB", name: "A"))
        store.addItem(Item(rfidTag: "BB", name: "B"))
        XCTAssertEqual(store.items.map(\.name), ["A"])
    }

    func testRemovingContainerMovesItemsToLoose() {
        let store = ContainerStore(fileURL: fileURL)
        let box = Container(rfidTag: "AA", name: "Box")
        store.addContainer(box)
        let item = Item(rfidTag: "BB", name: "PLA", containerId: box.id)
        store.addItem(item)

        store.removeContainer(id: box.id)

        XCTAssertNil(store.container(id: box.id))
        XCTAssertEqual(store.items(in: Container.looseId).map(\.id), [item.id])
    }

    func testStandardContainersCannotBeRemoved() {
        let store = ContainerStore(fileURL: fileURL)
        store.removeContainer(id: Container.looseId)
        XCTAssertNotNil(store.container(id: Container.looseId))
    }

    func testMoveAndRemoveFromContainer() {
        let store = ContainerStore(fileURL: fileURL)
        let item = Item(rfidTag: "BB", name: "PLA", containerId: Container.looseId)
        store.addItem(item)

        store.moveItem(id: item.id, toContainer: Container.printerId)
        XCTAssertEqual(store.items(in: Container.printerId).map(\.id), [item.id])
        XCTAssertTrue(store.items(in: Container.looseId).isEmpty)

        store.removeItemFromContainer(id: item.id)
        XCTAssertNil(store.findItem(byTag: "BB")?.containerId)
    }
}
