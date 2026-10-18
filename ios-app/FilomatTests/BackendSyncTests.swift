import XCTest
@testable import Filomat

/// Liefert feste Antworten statt echter Netzwerkaufrufe.
final class StubURLProtocol: URLProtocol {
    nonisolated(unsafe) static var responses: [String: (Int, String)] = [:]
    nonisolated(unsafe) static var requests: [URLRequest] = []

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.requests.append(request)
        let (status, body) = Self.responses[request.url!.path] ?? (404, "")
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: Data(body.utf8))
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}
}

@MainActor
final class BackendSyncTests: XCTestCase {
    private let fileURL = FileManager.default.temporaryDirectory
        .appendingPathComponent(UUID().uuidString)
        .appendingPathComponent("store.json")

    private var client: BackendClient {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return BackendClient(baseURL: URL(string: "http://nas.local:8080")!, apiKey: "geheim",
                             session: URLSession(configuration: configuration))
    }

    /// Antwort von `/api/snapshot`, wie sie das Backend liefert.
    private let backendSnapshot = """
    {"containers":[
      {"id":"standard_drucker","rfidTag":"STANDARD_DRUCKER","name":"Drucker","description":"","capacity":null,"createdAt":1700000000000,"isStandard":true},
      {"id":"standard_lose","rfidTag":"STANDARD_LOSE","name":"Lose","description":"","capacity":null,"createdAt":1700000000000,"isStandard":true},
      {"id":"android-box","rfidTag":"04AABBCC","name":"Box aus Android","description":"Regal","capacity":6,"createdAt":1700000000123,"isStandard":false}],
     "items":[
      {"id":"spool-1","rfidTag":"04112233","name":"PLA Rot","description":"","createdAt":1700000100000,"containerId":"android-box",
       "itemData":{"protocol":"openspool","version":null,"type":"PLA","color_hex":"FF0000","brand":null,"min_temp":"190","max_temp":"220","bed_min_temp":null,"bed_max_temp":null,"variant":null,"color":null},
       "netWeight":1000,"remainingWeight":850.5}]}
    """

    override func setUp() {
        StubURLProtocol.requests = []
        StubURLProtocol.responses = [
            "/api/snapshot": (200, backendSnapshot),
            "/api/import": (200, #"{"containers":1,"items":0,"skipped":[]}"#),
        ]
    }

    func testConnectUploadsLocalDataAndTakesOverBackendState() async throws {
        let store = ContainerStore(fileURL: fileURL)
        store.addContainer(Container(rfidTag: "LOCAL", name: "iPhone-Box"))

        let result = await store.connect(client)

        XCTAssertEqual(result?.containers, 1)
        XCTAssertNil(store.syncError)
        let upload = try XCTUnwrap(StubURLProtocol.requests.first { $0.url?.path == "/api/import" })
        XCTAssertEqual(upload.value(forHTTPHeaderField: "X-Api-Key"), "geheim")
        // Container aus der Android-App sind danach auf dem iPhone bekannt
        XCTAssertEqual(store.findContainer(byTag: "04AABBCC")?.name, "Box aus Android")
        XCTAssertEqual(store.items(in: "android-box").first?.remainingWeight, 850.5)
        XCTAssertEqual(store.items.first?.itemData?.colorHex, "FF0000")

        let reloaded = ContainerStore(fileURL: fileURL)
        XCTAssertEqual(reloaded.containers.count, 3)
    }

    func testUnauthorizedIsReported() async {
        StubURLProtocol.responses["/api/import"] = (401, "")
        let store = ContainerStore(fileURL: fileURL)

        let result = await store.connect(client)

        XCTAssertNil(result)
        XCTAssertEqual(store.syncError, BackendError.unauthorized.errorDescription)
    }

    func testEncodesDatesAsWholeMilliseconds() throws {
        let item = Item(rfidTag: "A", name: "A", createdAt: Date(timeIntervalSince1970: 1_700_000_000.1234))
        let json = String(decoding: try BackendClient.encoder.encode(item), as: UTF8.self)
        XCTAssertTrue(json.contains("\"createdAt\":1700000000123"), json)
    }

    func testSettingsRequireHttpAddress() {
        XCTAssertNil(BackendSettings.client(serverURL: "nas.local", apiKey: ""))
        XCTAssertNotNil(BackendSettings.client(serverURL: "http://192.168.1.10:8080", apiKey: ""))
    }
}
