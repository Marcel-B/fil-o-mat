import Foundation

/// Alle Container und Filamente im Austauschformat des Backends (`/api/snapshot`, `/api/import`).
struct BackendSnapshot: Codable {
    var containers: [Container]
    var items: [Item]
}

struct ImportResult: Codable {
    var containers: Int
    var items: Int
    var skipped: [String]
}

/// Stand der Backend-Version (`/api/update`). Neue Commits prüft ein Updater auf dem Server.
struct BackendUpdateStatus: Codable, Equatable {
    struct Commit: Codable, Equatable, Identifiable {
        var sha: String
        var subject: String
        var date: Date
        var id: String { sha }
    }

    /// `false`, wenn auf dem Server kein Updater eingerichtet ist.
    var supported: Bool
    var branch: String?
    var currentVersion: String?
    var currentDate: Date?
    var latestVersion: String?
    var updateAvailable: Bool
    var commits: [Commit]
    var checkedAt: Date?
    /// `idle`, `requested`, `running` oder `failed`
    var state: String
    var message: String?

    var isUpdating: Bool { state == "requested" || state == "running" }
}

enum BackendError: LocalizedError {
    case unauthorized
    case status(Int)

    var errorDescription: String? {
        switch self {
        case .unauthorized: "Der Server lehnt den API-Schlüssel ab."
        case .status(let code): "Der Server antwortet mit Fehler \(code)."
        }
    }
}

/// Zugriff auf das Fil-o-mat-Backend.
struct BackendClient {
    var baseURL: URL
    var apiKey: String?
    var session: URLSession = .shared

    func snapshot() async throws -> BackendSnapshot {
        let data = try await send(request("api/snapshot", method: "GET"))
        return try Self.decoder.decode(BackendSnapshot.self, from: data)
    }

    /// Legt neue Einträge an und aktualisiert vorhandene; löscht nichts.
    @discardableResult
    func importData(_ snapshot: BackendSnapshot) async throws -> ImportResult {
        var request = request("api/import", method: "POST")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(snapshot)
        return try Self.decoder.decode(ImportResult.self, from: try await send(request))
    }

    func deleteContainer(id: String) async throws {
        try await send(request("api/containers/\(id)", method: "DELETE"), allowNotFound: true)
    }

    func deleteItem(id: String) async throws {
        try await send(request("api/items/\(id)", method: "DELETE"), allowNotFound: true)
    }

    /// Leergewichte von Spulen pro Hersteller (aus der SpoolmanDB, über das Backend).
    func spoolWeights() async throws -> [String: Double] {
        try Self.decoder.decode([String: Double].self, from: try await send(request("api/spool-weights", method: "GET")))
    }

    // MARK: - Drucker

    func printers() async throws -> [Printer] {
        try Self.decoder.decode([Printer].self, from: try await send(request("api/printers", method: "GET")))
    }

    @discardableResult
    func createPrinter(_ printer: PrinterRequest) async throws -> Printer {
        var request = request("api/printers", method: "POST")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(printer)
        return try Self.decoder.decode(Printer.self, from: try await send(request))
    }

    func deletePrinter(id: String) async throws {
        try await send(request("api/printers/\(id)", method: "DELETE"), allowNotFound: true)
    }

    func syncPrinter(id: String) async throws -> SyncResponse {
        try Self.decoder.decode(SyncResponse.self, from: try await send(request("api/printers/\(id)/sync", method: "POST")))
    }

    func consumption(itemId: String) async throws -> [ConsumptionEntry] {
        var components = URLComponents(url: baseURL.appendingPathComponent("api/consumption"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "itemId", value: itemId)]
        var request = request("api/consumption", method: "GET")
        request.url = components.url
        return try Self.decoder.decode([ConsumptionEntry].self, from: try await send(request))
    }

    // MARK: - Aktualisierung

    func updateStatus() async throws -> BackendUpdateStatus {
        try Self.decoder.decode(BackendUpdateStatus.self, from: try await send(request("api/update", method: "GET")))
    }

    /// Lässt den Server sofort nach neuen Commits suchen; das Ergebnis liefert später `updateStatus()`.
    func checkForUpdate() async throws {
        try await send(request("api/update/check", method: "POST"))
    }

    /// Spielt die neue Version ein; das Backend startet dabei neu.
    func installUpdate() async throws {
        try await send(request("api/update", method: "POST"))
    }

    private func request(_ path: String, method: String) -> URLRequest {
        var request = URLRequest(url: baseURL.appendingPathComponent(path), timeoutInterval: 15)
        request.httpMethod = method
        if let apiKey, !apiKey.isEmpty { request.setValue(apiKey, forHTTPHeaderField: "X-Api-Key") }
        return request
    }

    @discardableResult
    private func send(_ request: URLRequest, allowNotFound: Bool = false) async throws -> Data {
        let (data, response) = try await session.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 401 { throw BackendError.unauthorized }
        if status == 404 && allowNotFound { return data }
        guard (200..<300).contains(status) else { throw BackendError.status(status) }
        return data
    }

    /// Zeitstempel als ganze Millisekunden, wie in der Android-App und im Backend.
    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(Int64((date.timeIntervalSince1970 * 1000).rounded()))
        }
        return encoder
    }()

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        return decoder
    }()
}

/// Gespeicherte Server-Adresse und API-Schlüssel.
enum BackendSettings {
    private static let urlKey = "backendURL"
    private static let apiKeyKey = "backendApiKey"

    static var serverURL: String {
        get { UserDefaults.standard.string(forKey: urlKey) ?? "" }
        set { UserDefaults.standard.set(newValue.trimmingCharacters(in: .whitespaces), forKey: urlKey) }
    }

    static var apiKey: String {
        get { UserDefaults.standard.string(forKey: apiKeyKey) ?? "" }
        set { UserDefaults.standard.set(newValue.trimmingCharacters(in: .whitespaces), forKey: apiKeyKey) }
    }

    static func client(serverURL: String = serverURL, apiKey: String = apiKey) -> BackendClient? {
        guard let url = URL(string: serverURL.trimmingCharacters(in: .whitespaces)),
              url.scheme == "http" || url.scheme == "https", url.host != nil else { return nil }
        return BackendClient(baseURL: url, apiKey: apiKey.isEmpty ? nil : apiKey)
    }
}
