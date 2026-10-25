import Foundation

/// Alle Container und Filamente im Austauschformat des Backends (`/api/snapshot`, `/api/import`).
struct BackendSnapshot: Codable {
    var containers: [Container]
    var items: [Item]
    /// Angebrochene Spulen; nur in der Antwort des Backends, beim Import ignoriert.
    var spools: [Spool]? = nil
}

/// Spule, die beim Zusammenführen eine neue Nummer bekommen hat.
struct RenumberedSpool: Codable, Hashable {
    var oldNumber: Int
    var newNumber: Int
}

private struct MergeResult: Decodable {
    var renumbered: [RenumberedSpool]
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

/// Stand der Nextcloud-Sicherung (`/api/backup`).
struct BackupStatus: Codable, Equatable {
    struct Run: Codable, Equatable {
        var startedAt: Date
        var finishedAt: Date
        var success: Bool
        var archives: [String]
        var bytes: Int64
        var error: String?
        var scheduled: Bool
    }

    /// `false`, solange auf dem Server keine Nextcloud eingetragen ist.
    var configured: Bool
    var running: Bool
    var last: Run?
    var nextRun: Date?
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

    // MARK: - Spulen

    /// Neue Spule der Sorte anbrechen; ohne Restgewicht ist sie voll, der Vorrat sinkt um eins.
    @discardableResult
    func openSpool(itemId: String, remainingWeight: Double? = nil, printerId: String? = nil, tool: Int? = nil,
                   containerId: String? = nil) async throws -> Spool {
        var request = request("api/items/\(itemId)/spools", method: "POST")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(SpoolUpdate(remainingWeight: remainingWeight, printerId: printerId, tool: tool, containerId: containerId))
        return try Self.decoder.decode(Spool.self, from: try await send(request))
    }

    /// Führt das Filament `fromId` in `itemId` zusammen; liefert die Spulen, die eine neue Nummer bekommen haben.
    func mergeItem(into itemId: String, from fromId: String) async throws -> [RenumberedSpool] {
        var request = request("api/items/\(itemId)/merge", method: "POST")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(["fromId": fromId])
        return try Self.decoder.decode(MergeResult.self, from: try await send(request)).renumbered
    }

    /// Restgewicht und Platz setzen; was am Zielplatz steckt, wandert ins Lager.
    @discardableResult
    func updateSpool(id: String, _ update: SpoolUpdate) async throws -> Spool {
        var request = request("api/spools/\(id)", method: "PUT")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(update)
        return try Self.decoder.decode(Spool.self, from: try await send(request))
    }

    func markSpoolEmpty(id: String) async throws {
        try await send(request("api/spools/\(id)/empty", method: "POST"))
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

    @discardableResult
    func updatePrinter(id: String, _ update: PrinterUpdateRequest) async throws -> Printer {
        var request = request("api/printers/\(id)", method: "PUT")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(update)
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

    /// Zustand aller Drucker mit Spulen pro Kopf und Hinweisen; das Backend fragt nach, was älter als 10 s ist.
    func printerStatus() async throws -> [PrinterLive] {
        try Self.decoder.decode([PrinterLive].self, from: try await send(request("api/printers/status", method: "GET"), timeout: 30))
    }

    /// Vorschaubild des laufenden Drucks; `nil`, wenn der Drucker keins liefert.
    func printerThumbnail(id: String) async throws -> Data? {
        var request = request("api/printers/\(id)/thumbnail", method: "GET")
        // Bei Bambu lädt das Backend dafür die ganze Druckdatei
        request.timeoutInterval = 60
        let data = try await send(request, allowNotFound: true)
        return data.isEmpty ? nil : data
    }

    func stats(months: Int) async throws -> StatsResponse {
        var components = URLComponents(url: baseURL.appendingPathComponent("api/stats"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "months", value: String(months))]
        var request = request("api/stats", method: "GET")
        request.url = components.url
        return try Self.decoder.decode(StatsResponse.self, from: try await send(request))
    }

    // MARK: - Meldungen

    func notificationSettings() async throws -> NotificationSettings {
        try Self.decoder.decode(NotificationSettings.self, from: try await send(request("api/settings/notifications", method: "GET")))
    }

    @discardableResult
    func saveNotificationSettings(_ settings: NotificationSettingsRequest) async throws -> NotificationSettings {
        var request = request("api/settings/notifications", method: "PUT")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try Self.encoder.encode(settings)
        return try Self.decoder.decode(NotificationSettings.self, from: try await send(request))
    }

    func sendTestNotification() async throws {
        try await send(request("api/settings/notifications/test", method: "POST"))
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

    // MARK: - Sicherung

    func backupStatus() async throws -> BackupStatus {
        try Self.decoder.decode(BackupStatus.self, from: try await send(request("api/backup", method: "GET")))
    }

    /// Startet eine Sicherung; wie sie lief, liefert danach `backupStatus()`.
    func startBackup() async throws {
        try await send(request("api/backup", method: "POST"))
    }

    private func request(_ path: String, method: String) -> URLRequest {
        var request = URLRequest(url: baseURL.appendingPathComponent(path), timeoutInterval: 15)
        request.httpMethod = method
        if let apiKey, !apiKey.isEmpty { request.setValue(apiKey, forHTTPHeaderField: "X-Api-Key") }
        return request
    }

    @discardableResult
    private func send(_ request: URLRequest, allowNotFound: Bool = false, timeout: TimeInterval? = nil) async throws -> Data {
        var request = request
        if let timeout { request.timeoutInterval = timeout }
        let (data, response) = try await session.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 401 { throw BackendError.unauthorized }
        if status == 404 && allowNotFound { return Data() }
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
