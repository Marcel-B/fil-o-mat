import XCTest
@testable import Filomat

@MainActor
final class PlaceFlowTests: XCTestCase {
    private func makeStore() -> ContainerStore {
        ContainerStore(fileURL: FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathComponent("store.json"))
    }

    private var stubClient: BackendClient {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return BackendClient(baseURL: URL(string: "http://nas.local:8080")!, apiKey: nil,
                             session: URLSession(configuration: configuration))
    }

    /// Eine Sorte mit Vorrat und einer angebrochenen Spule im Lager, dazu eine Box.
    private func storeWithSpool() -> (ContainerStore, Item, Container) {
        let store = makeStore()
        let box = Container(rfidTag: "BOX", name: "Box")
        let item = Item(rfidTag: "PLA", name: "PLA Weiß", containerId: Container.looseId, spoolsInStock: 2)
        store.apply(BackendSnapshot(
            containers: Container.standardContainers + [box],
            items: [item],
            spools: [Spool(id: "s1", itemId: item.id, number: 1, remainingWeight: 430)]
        ))
        return (store, item, box)
    }

    func testStoringUnknownFilamentCreatesItAndMovesTheSort() async {
        let store = makeStore()
        let box = Container(rfidTag: "BOX", name: "Box")
        store.addContainer(box)
        let model = PlaceFlowModel(target: .container)

        model.handleFilamentTag(ScannedTag(tagId: "NEW", itemData: ItemJsonData(type: "PETG", brand: "Sunlu")), store: store)
        // Ohne angebrochene Spulen gibt es nur die Sorte, der Ablauf springt direkt zum Container
        XCTAssertEqual(model.item?.name, "Sunlu PETG")
        XCTAssertEqual(model.choice, .stock)
        XCTAssertEqual(model.step, .chooseTarget)
        XCTAssertFalse(model.canPerform(store: store))

        model.handleTargetTag(ScannedTag(tagId: "BOX"), store: store)
        XCTAssertTrue(model.canPerform(store: store))
        await model.perform(store: store)

        XCTAssertEqual(store.items(in: box.id).map(\.rfidTag), ["NEW"])
        XCTAssertEqual(model.step, .scanFilament)
        XCTAssertNotNil(model.doneMessage)
    }

    func testContainerTagFirstIsRejected() {
        let (store, _, _) = storeWithSpool()
        let model = PlaceFlowModel(target: .container)
        model.handleFilamentTag(ScannedTag(tagId: "BOX"), store: store)
        XCTAssertNil(model.item)
        XCTAssertNotNil(model.errorMessage)
    }

    func testOffersSortAndOpenedSpools() {
        let (store, item, box) = storeWithSpool()
        let model = PlaceFlowModel(target: .container)
        model.handleFilamentTag(ScannedTag(tagId: "PLA"), store: store)

        XCTAssertEqual(model.step, .chooseSpool)
        XCTAssertEqual(model.choices(store: store), [.stock, .spool("s1")])

        model.choose(.stock, store: store)
        model.selectContainer(store.container(id: Container.looseId)!, store: store)
        XCTAssertTrue(model.isAlreadyThere(store: store), "Die Sorte liegt schon in „Lose“")
        XCTAssertFalse(model.canPerform(store: store))

        model.selectContainer(box, store: store)
        XCTAssertFalse(model.isAlreadyThere(store: store))
        XCTAssertEqual(model.currentPlaceText(store: store), "Lose")
        XCTAssertEqual(store.item(id: item.id)?.spoolsInStock, 2)
    }

    func testPrinterFlowNeedsBackendAndOffersNewSpool() {
        let (store, _, _) = storeWithSpool()
        let model = PlaceFlowModel(target: .printer)
        model.handleFilamentTag(ScannedTag(tagId: "PLA"), store: store)
        XCTAssertTrue(model.choices(store: store).isEmpty)

        store.backend = stubClient
        model.reset()
        model.handleFilamentTag(ScannedTag(tagId: "PLA"), store: store)
        XCTAssertEqual(model.choices(store: store), [.spool("s1"), .newSpool])
        XCTAssertEqual(model.actionTitle, "Einlegen")

        model.choose(.newSpool, store: store)
        XCTAssertEqual(model.actionTitle, "Anbrechen und einlegen")
        XCTAssertFalse(model.canPerform(store: store), "Ohne Drucker und Kopf geht es nicht weiter")
        model.selectPlace(.printer("u1", tool: 2))
        XCTAssertTrue(model.canPerform(store: store))
    }

    func testStoringOpenedSpoolSendsContainer() async throws {
        let (store, _, box) = storeWithSpool()
        store.backend = stubClient
        StubURLProtocol.requests = []
        StubURLProtocol.responses = [
            "/api/spools/s1": (200, #"{"id":"s1","itemId":"x","number":1,"remainingWeight":430,"printerId":null,"tool":null,"containerId":"\#(box.id)"}"#),
        ]
        let model = PlaceFlowModel(target: .container)
        model.handleFilamentTag(ScannedTag(tagId: "PLA"), store: store)
        model.choose(.spool("s1"), store: store)
        model.handleTargetTag(ScannedTag(tagId: "BOX"), store: store)

        await model.perform(store: store)

        let put = try XCTUnwrap(StubURLProtocol.requests.first { $0.httpMethod == "PUT" })
        let body = try XCTUnwrap(put.httpBody ?? put.httpBodyStream.map(Self.readAll))
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
        XCTAssertEqual(json["containerId"] as? String, box.id)
        XCTAssertEqual(json["remainingWeight"] as? Double, 430)
        XCTAssertNil(json["printerId"])
    }

    func testSpoolPlaceText() {
        let box = Container(id: "box", rfidTag: "BOX", name: "Regal oben")
        XCTAssertEqual(SpoolPlace.container("box").text(printers: [], containers: [box]), "Regal oben")
        XCTAssertEqual(SpoolPlace.storage.text(printers: [], containers: [box]), "Lager")
        let spool = Spool(id: "s", itemId: "i", number: 1, printerId: "p", tool: 1, containerId: "box")
        XCTAssertEqual(spool.place, .printer("p", tool: 1), "Im Drucker zählt der Container nicht")
    }

    private static func readAll(_ stream: InputStream) -> Data {
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 1024)
        while stream.hasBytesAvailable {
            let count = stream.read(&buffer, maxLength: buffer.count)
            if count <= 0 { break }
            data.append(buffer, count: count)
        }
        return data
    }
}
