import XCTest
@testable import Filomat

@MainActor
final class ScanViewModelTests: XCTestCase {
    private func makeStore() -> ContainerStore {
        ContainerStore(fileURL: FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathComponent("store.json"))
    }

    func testAddToContainerCreatesUnknownItem() {
        let store = makeStore()
        let box = Container(rfidTag: "BOX", name: "Box")
        store.addContainer(box)
        let model = ScanViewModel()

        model.handleScannedTag(ScannedTag(tagId: "BOX"), store: store)
        XCTAssertEqual(model.scannedContainer?.id, box.id)
        XCTAssertEqual(model.currentStep, 1)

        let data = ItemJsonData(type: "PETG", brand: "Sunlu")
        model.handleScannedTag(ScannedTag(tagId: "SPOOL", itemData: data), store: store)
        XCTAssertEqual(model.scannedItem?.name, "Sunlu PETG")
        XCTAssertTrue(model.canPerformAction)

        model.performAction(store: store)
        XCTAssertEqual(store.items(in: box.id).map(\.rfidTag), ["SPOOL"])
        XCTAssertEqual(model.currentStep, 0)
    }

    func testUnknownContainerShowsError() {
        let store = makeStore()
        let model = ScanViewModel()
        model.handleScannedTag(ScannedTag(tagId: "NOPE"), store: store)
        XCTAssertNotNil(model.errorMessage)
        XCTAssertEqual(model.currentStep, 0)
    }

    func testItemInOtherContainerAsksForConfirmation() {
        let store = makeStore()
        let box = Container(rfidTag: "BOX", name: "Box")
        store.addContainer(box)
        store.addItem(Item(rfidTag: "SPOOL", name: "PLA", containerId: Container.looseId))
        let model = ScanViewModel()

        model.handleScannedTag(ScannedTag(tagId: "BOX"), store: store)
        model.handleScannedTag(ScannedTag(tagId: "SPOOL"), store: store)
        XCTAssertTrue(model.showConfirmMove)
        XCTAssertFalse(model.canPerformAction)

        model.confirmMove()
        model.performAction(store: store)
        XCTAssertEqual(store.items(in: box.id).map(\.rfidTag), ["SPOOL"])
    }

    func testRemoveFromContainer() {
        let store = makeStore()
        store.addItem(Item(rfidTag: "SPOOL", name: "PLA", containerId: Container.printerId))
        let model = ScanViewModel()
        model.setScanMode(.removeFromContainer)

        model.handleScannedTag(ScannedTag(tagId: "SPOOL"), store: store)
        model.handleScannedTag(ScannedTag(tagId: "STANDARD_DRUCKER"), store: store)
        model.performAction(store: store)

        XCTAssertNil(store.findItem(byTag: "SPOOL")?.containerId)
    }
}
