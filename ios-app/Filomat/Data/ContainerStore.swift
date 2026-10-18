import Foundation
import Observation

/// Lokale Datenhaltung für Container und Filamente (JSON-Datei in Application Support).
///
/// Anders als die Android-App gibt es nur eine Quelle für die Zuordnung: `Item.containerId`.
/// Dadurch können Container- und Item-Liste nicht auseinanderlaufen.
@MainActor
@Observable
final class ContainerStore {
    private(set) var containers: [Container] = []
    private(set) var items: [Item] = []

    @ObservationIgnored private let fileURL: URL
    @ObservationIgnored private var standardContainersInitialized = false

    private struct Snapshot: Codable {
        var containers: [Container]
        var items: [Item]
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
        items.first { $0.rfidTag == rfidTag }
    }

    // MARK: - Container

    func addContainer(_ container: Container) {
        containers.append(container)
        save()
    }

    func updateContainer(_ container: Container) {
        guard let index = containers.firstIndex(where: { $0.id == container.id }) else { return }
        containers[index] = container
        save()
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
    }

    // MARK: - Items

    /// Fügt ein Item hinzu, sofern noch kein Item mit demselben (nicht leeren) Tag existiert.
    func addItem(_ item: Item) {
        let tag = item.rfidTag.trimmingCharacters(in: .whitespaces)
        if !tag.isEmpty, items.contains(where: { $0.rfidTag == item.rfidTag }) { return }
        items.append(item)
        save()
    }

    func updateItem(_ item: Item) {
        guard let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        items[index] = item
        save()
    }

    func removeItem(id: String) {
        items.removeAll { $0.id == id }
        save()
    }

    /// Lagert ein Item in einen Container ein (verschiebt es ggf. aus seinem bisherigen Container).
    func moveItem(id: String, toContainer containerId: String) {
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        var item = items.remove(at: index)
        item.containerId = containerId
        // Wie in der Android-App erscheint das Item am Ende der Container-Liste.
        items.append(item)
        save()
    }

    /// Nimmt ein Item aus seinem Container heraus; danach ist es keinem Container zugeordnet.
    func removeItemFromContainer(id: String) {
        guard let index = items.firstIndex(where: { $0.id == id }) else { return }
        items[index].containerId = nil
        save()
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
            standardContainersInitialized = snapshot.standardContainersInitialized
        } catch {
            print("ContainerStore: Laden fehlgeschlagen: \(error)")
        }
    }

    private func save() {
        let snapshot = Snapshot(
            containers: containers,
            items: items,
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
