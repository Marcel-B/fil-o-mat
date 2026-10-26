import Foundation
import Observation

/// Lokale Datenhaltung für Container und Filamente (JSON-Datei in Application Support).
///
/// Anders als die Android-App gibt es nur eine Quelle für die Zuordnung: `Item.containerId`.
/// Dadurch können Container- und Item-Liste nicht auseinanderlaufen.
///
/// Ist ein Backend verbunden, wird jede Änderung dorthin übertragen und `refresh()`
/// übernimmt den Stand des Backends.
@MainActor
@Observable
final class ContainerStore {
    private(set) var containers: [Container] = []
    private(set) var items: [Item] = []
    /// Angebrochene Spulen, wie sie das Backend führt.
    private(set) var spools: [Spool] = []
    /// Drucker aus dem Backend, für die Platzangabe der Spulen.
    private(set) var printers: [Printer] = []
    private(set) var isSyncing = false
    private(set) var syncError: String?
    private(set) var lastSyncedAt: Date?
    /// Stand der Backend-Version; `nil`, solange unbekannt oder ohne Updater auf dem Server.
    private(set) var updateStatus: BackendUpdateStatus?
    /// Passen App und Backend zusammen? `nil`, solange unbekannt (z. B. Backend nicht erreichbar).
    private(set) var compatibility: ApiCompatibility?
    /// Stand der Nextcloud-Sicherung; `nil`, solange unbekannt oder bei einem Backend ohne `/api/backup`.
    private(set) var backupStatus: BackupStatus?
    /// Zustand der Drucker pro Drucker-ID; leer bei einem Backend ohne `/api/printers/status`.
    private(set) var printerLive: [String: PrinterLive] = [:]

    @ObservationIgnored var backend: BackendClient?

    @ObservationIgnored private let fileURL: URL
    @ObservationIgnored private var standardContainersInitialized = false

    private struct Snapshot: Codable {
        var containers: [Container]
        var items: [Item]
        var spools: [Spool]?
        var printers: [Printer]?
        var standardContainersInitialized: Bool
    }

    init(fileURL: URL = ContainerStore.defaultFileURL) {
        self.fileURL = fileURL
        load()
        initializeStandardContainers()
    }

    nonisolated static var defaultFileURL: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return dir.appendingPathComponent("container_store.json")
    }

    // MARK: - Abfragen

    func container(id: String) -> Container? {
        containers.first { $0.id == id }
    }

    func items(in containerId: String) -> [Item] {
        items.filter { $0.containerId == containerId }
    }

    func findContainer(byTag rfidTag: String) -> Container? {
        containers.first { $0.rfidTag == rfidTag }
    }

    func findItem(byTag rfidTag: String) -> Item? {
        items.first { $0.hasTag(rfidTag) }
    }

    func spools(of itemId: String) -> [Spool] {
        spools.filter { $0.itemId == itemId }.sorted { $0.number < $1.number }
    }

    /// Andere Einträge derselben Sorte (gleiche Marke, Typ, Variante und Farbe).
    func possibleDuplicates(of item: Item) -> [Item] {
        guard let key = item.duplicateKey else { return [] }
        return items.filter { $0.id != item.id && $0.duplicateKey == key }
    }

    func item(id: String) -> Item? {
        items.first { $0.id == id }
    }

    /// Angebrochene Spulen, die im Lager in diesem Container liegen; "Lose" zeigt auch die ohne (bekannten) Container.
    func spools(inContainer containerId: String) -> [Spool] {
        spools.filter { spool in
            guard spool.printerId == nil else { return false }
            if spool.containerId == containerId { return true }
            return containerId == Container.looseId && (spool.containerId.map { container(id: $0) == nil } ?? true)
        }
    }

    /// Die Spule, die in diesem Kopf steckt.
    func spool(inPrinter printerId: String, tool: Int) -> Spool? {
        spools.first { $0.printerId == printerId && ($0.tool ?? 0) == tool }
    }

    /// Container, in die man Filament einlagern kann: alle außer denen der Drucker.
    var storageContainers: [Container] {
        containers.filter { $0.id != Container.printerId && printer(owningContainer: $0.id) == nil }
    }

    /// Der Drucker, dem dieser Container gehört (jeder Drucker im Backend hat einen).
    func printer(owningContainer containerId: String) -> Printer? {
        printers.first { $0.containerId == containerId }
    }

    /// Spulen, die die Übersicht für diesen Container zählt: was in den Köpfen seines Druckers steckt und was darin liegt.
    func spoolCount(in containerId: String) -> Int {
        let loaded = printer(owningContainer: containerId).map { printer in
            (0..<printer.toolCount).filter { spool(inPrinter: printer.id, tool: $0) != nil }.count
        } ?? 0
        return loaded + spools(inContainer: containerId).count
    }

    // MARK: - Container

    func addContainer(_ container: Container) {
        containers.append(container)
        save()
        push(containers: [container])
    }

    func updateContainer(_ container: Container) {
        guard let index = containers.firstIndex(where: { $0.id == container.id }) else { return }
        containers[index] = container
        save()
        push(containers: [container])
    }

    /// Löscht einen Container. Standard-Container bleiben erhalten,
    /// enthaltene Filamente wandern in den Container "Lose".
    func removeContainer(id: String) {
        guard let container = container(id: id), !container.isStandard else { return }
        let target = self.container(id: Container.looseId)?.id
        for index in items.indices where items[index].containerId == id {
            items[index].containerId = target
        }
        containers.removeAll { $0.id == id }
        save()
        remote { try await $0.deleteContainer(id: id) }
    }

    // MARK: - Items

    /// Fügt ein Item hinzu, sofern noch kein Item mit demselben (nicht leeren) Tag existiert.
    func addItem(_ item: Item) {
        let tag = item.rfidTag.trimmingCharacters(in: .whitespaces)
        if !tag.isEmpty, findItem(byTag: item.rfidTag) != nil { return }
        items.append(item)
        save()
        push(items: [item])
    }

    /// - Parameter sendWeights: Gewichte mit ans Backend schicken (nur wenn sie bewusst geändert wurden).
    func updateItem(_ item: Item, sendWeights: Bool = false) {
        guard let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        items[index] = item
        save()
        push(items: [item], sendWeights: sendWeights)
    }

    func removeItem(id: String) {
        items.removeAll { $0.id == id }
        save()
        remote { try await $0.deleteItem(id: id) }
    }

    /// Fügt einem Filament ein weiteres Tag hinzu, z. B. ein Musterplättchen an einer Box oder einem Drucker.
    /// Gehört das Tag bisher einem anderen Filament, wird es dort entfernt; war es dessen einziges Tag,
    /// wird der Eintrag gelöscht (nur ohne angebrochene Spulen).
    func addTag(_ tag: String, toItem itemId: String) async throws {
        if let previous = findItem(byTag: tag), previous.id != itemId {
            if previous.allTags.count > 1 {
                try await removeTag(tag, fromItem: previous.id)
            } else {
                guard spools(of: previous.id).isEmpty else {
                    throw StoreError.tagInUse("Das Tag gehört zu „\(previous.displayTitle)“, das angebrochene Spulen hat.")
                }
                try await backend?.deleteItem(id: previous.id)
                items.removeAll { $0.id == previous.id }
            }
        }
        guard let index = items.firstIndex(where: { $0.id == itemId }), !items[index].hasTag(tag) else { return }
        if items[index].rfidTag.isEmpty {
            items[index].rfidTag = tag
        } else {
            items[index].extraTags = (items[index].extraTags ?? []) + [tag]
        }
        try await saveTags(of: items[index])
    }

    /// Entfernt ein Tag von einem Filament. Ist es das Haupt-Tag, rückt das erste weitere Tag nach;
    /// das letzte Tag bleibt.
    func removeTag(_ tag: String, fromItem itemId: String) async throws {
        guard let index = items.firstIndex(where: { $0.id == itemId }) else { return }
        var item = items[index]
        var extra = item.extraTags ?? []
        if item.rfidTag == tag {
            guard !extra.isEmpty else { throw StoreError.tagInUse("Das letzte Tag eines Filaments bleibt.") }
            item.rfidTag = extra.removeFirst()
        } else {
            extra.removeAll { $0 == tag }
        }
        item.extraTags = extra
        items[index] = item
        try await saveTags(of: item)
    }

    private func saveTags(of item: Item) async throws {
        save()
        guard let backend else { return }
        let result = try await backend.importData(BackendSnapshot(containers: [], items: [Self.withoutWeights(item)]))
        if let reason = result.skipped.first { throw StoreError.tagInUse(reason) }
        await refresh()
    }

    /// Führt ein doppeltes Filament in dieses zusammen (Spulen, Verbrauch, Vorrat, Tags); das andere wird gelöscht.
    /// - Returns: Spulen, die eine neue Nummer bekommen haben.
    func mergeItem(_ fromId: String, into itemId: String) async throws -> [RenumberedSpool] {
        guard let backend else { throw StoreError.tagInUse("Zum Zusammenführen muss das Backend verbunden sein.") }
        let renumbered = try await backend.mergeItem(into: itemId, from: fromId)
        items.removeAll { $0.id == fromId }
        save()
        await refresh()
        return renumbered
    }

    /// Lagert ein Item in einen Container ein (verschiebt es ggf. aus seinem bisherigen Container).
    func moveItem(id: String, toContainer containerId: String) {
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        var item = items.remove(at: index)
        item.containerId = containerId
        // Wie in der Android-App erscheint das Item am Ende der Container-Liste.
        items.append(item)
        save()
        push(items: [item])
    }

    /// Nimmt ein Item aus seinem Container heraus; danach ist es keinem Container zugeordnet.
    func removeItemFromContainer(id: String) {
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        items[index].containerId = nil
        save()
        push(items: [items[index]])
    }

    // MARK: - Backend

    /// Verbindet mit dem Backend: lädt zuerst die lokalen Daten hoch (nichts wird gelöscht)
    /// und übernimmt danach den gemeinsamen Stand.
    @discardableResult
    func connect(_ client: BackendClient) async -> ImportResult? {
        backend = client
        isSyncing = true
        defer { isSyncing = false }
        do {
            let result = try await client.importData(BackendSnapshot(
                containers: containers.filter { !$0.isStandard },
                items: items.map(Self.withoutWeights)
            ))
            apply(try await client.snapshot())
            await loadPrinters(client)
            return result
        } catch {
            syncError = error.localizedDescription
            return nil
        }
    }

    func disconnect() {
        backend = nil
        syncError = nil
        lastSyncedAt = nil
        updateStatus = nil
        compatibility = nil
        backupStatus = nil
        printerLive = [:]
    }

    /// Zustand der Drucker; Fehler (z. B. ein älteres Backend) zählen nicht als Sync-Fehler.
    func refreshPrinterLive() async {
        guard let backend else { return }
        guard let live = try? await backend.printerStatus() else { return }
        printerLive = Dictionary(live.map { ($0.printerId, $0) }, uniquingKeysWith: { first, _ in first })
    }

    /// Fehler (z. B. ein älteres Backend ohne `/api/backup`) zählen nicht als Sync-Fehler.
    func refreshBackupStatus() async {
        guard let backend else { return }
        backupStatus = try? await backend.backupStatus()
    }

    /// Startet eine Sicherung und wartet, bis sie durch ist (höchstens eine Minute).
    func startBackup() async throws {
        guard let backend else { return }
        try await backend.startBackup()
        for _ in 0..<60 {
            await refreshBackupStatus()
            if backupStatus?.running != true { return }
            try await Task.sleep(for: .seconds(1))
        }
    }

    /// Fragt, ob es eine neue Backend-Version gibt. Fehler (z. B. ein älteres Backend ohne `/api/update`) zählen nicht als Sync-Fehler.
    func refreshUpdateStatus() async {
        guard let backend else { return }
        do {
            compatibility = ApiCompatibility(try await backend.version())
        } catch {
            // Nicht erreichbar (z. B. während des Neustarts): den letzten Stand behalten
        }
        guard let status = try? await backend.updateStatus() else {
            // Während des Neustarts antwortet das Backend kurz nicht; den Stand dann behalten
            if updateStatus?.isUpdating != true { updateStatus = nil }
            return
        }
        updateStatus = status.supported ? status : nil
    }

    func checkForUpdate() async throws {
        guard let backend else { return }
        try await backend.checkForUpdate()
    }

    func installUpdate() async throws {
        guard let backend else { return }
        try await backend.installUpdate()
        await refreshUpdateStatus()
    }

    /// Übernimmt den Stand des Backends (z. B. Container, die auf einem anderen Gerät angelegt wurden).
    func refresh() async {
        guard let backend, !isSyncing else { return }
        isSyncing = true
        defer { isSyncing = false }
        do {
            apply(try await backend.snapshot())
            await loadPrinters(backend)
        } catch {
            syncError = error.localizedDescription
            return
        }
        await refreshPrinterLive()
    }

    // MARK: - Spulen

    /// Bricht eine neue Spule der Sorte an (aus dem Vorrat) und lädt danach den Stand neu.
    @discardableResult
    func openSpool(itemId: String, remainingWeight: Double? = nil, place: SpoolPlace = .storage) async throws -> Spool? {
        guard let backend else { return nil }
        let spool = try await backend.openSpool(
            itemId: itemId, remainingWeight: remainingWeight,
            printerId: place.printerId, tool: place.tool, containerId: place.containerId
        )
        await refresh()
        return spool
    }

    func updateSpool(_ spool: Spool) async throws {
        guard let backend else { return }
        try await backend.updateSpool(id: spool.id, SpoolUpdate(
            remainingWeight: spool.remainingWeight, printerId: spool.printerId, tool: spool.tool, containerId: spool.containerId
        ))
        await refresh()
    }

    /// Legt eine angebrochene Spule an einen anderen Platz; was dort im Drucker steckte, wandert ins Lager.
    func placeSpool(_ spool: Spool, at place: SpoolPlace) async throws {
        var moved = spool
        moved.printerId = place.printerId
        moved.tool = place.tool
        moved.containerId = place.containerId
        try await updateSpool(moved)
    }

    func markSpoolEmpty(id: String) async throws {
        guard let backend else { return }
        try await backend.markSpoolEmpty(id: id)
        await refresh()
    }

    private func loadPrinters(_ client: BackendClient) async {
        guard let loaded = try? await client.printers() else { return }
        printers = loaded
        save()
    }

    func apply(_ snapshot: BackendSnapshot) {
        containers = snapshot.containers
        items = snapshot.items
        spools = snapshot.spools ?? []
        standardContainersInitialized = true
        syncError = nil
        lastSyncedAt = Date()
        save()
    }

    private func push(containers: [Container] = [], items: [Item] = [], sendWeights: Bool = false) {
        let snapshot = BackendSnapshot(containers: containers, items: sendWeights ? items : items.map(Self.withoutWeights))
        remote { _ = try await $0.importData(snapshot) }
    }

    private func remote(_ operation: @escaping (BackendClient) async throws -> Void) {
        guard let backend else { return }
        Task {
            do {
                try await operation(backend)
                syncError = nil
            } catch {
                syncError = error.localizedDescription
            }
        }
    }

    /// Die Gewichte pflegt das Backend; die App schickt sie nicht zurück, damit gebuchter Verbrauch
    /// nicht mit einem älteren Stand überschrieben wird.
    private static func withoutWeights(_ item: Item) -> Item {
        var item = item
        item.netWeight = nil
        item.spoolWeight = nil
        item.spoolsInStock = nil
        return item
    }

    // MARK: - Persistenz

    private func initializeStandardContainers() {
        guard !standardContainersInitialized else { return }
        for standard in Container.standardContainers where container(id: standard.id) == nil {
            containers.append(standard)
        }
        standardContainersInitialized = true
        save()
    }

    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else { return }
        do {
            let snapshot = try Self.decoder.decode(Snapshot.self, from: data)
            containers = snapshot.containers
            items = snapshot.items
            spools = snapshot.spools ?? []
            printers = snapshot.printers ?? []
            standardContainersInitialized = snapshot.standardContainersInitialized
        } catch {
            print("ContainerStore: Laden fehlgeschlagen: \(error)")
        }
    }

    private func save() {
        let snapshot = Snapshot(
            containers: containers,
            items: items,
            spools: spools,
            printers: printers,
            standardContainersInitialized: standardContainersInitialized
        )
        do {
            try FileManager.default.createDirectory(
                at: fileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            try Self.encoder.encode(snapshot).write(to: fileURL, options: .atomic)
        } catch {
            print("ContainerStore: Speichern fehlgeschlagen: \(error)")
        }
    }

    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .millisecondsSince1970
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return encoder
    }()

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        return decoder
    }()
}

enum StoreError: LocalizedError {
    case tagInUse(String)

    var errorDescription: String? {
        switch self {
        case .tagInUse(let message): message
        }
    }
}
