import Foundation
import Observation

/// Wohin ein Filament gebracht wird.
enum PlaceTarget {
    /// In einen Container im Lager
    case container
    /// In einen Kopf eines Druckers
    case printer
}

/// Was von einer Sorte bewegt wird.
enum SpoolChoice: Hashable {
    /// Die Sorte selbst: Musterplättchen mit den vollen Spulen im Vorrat.
    case stock
    /// Eine angebrochene Spule.
    case spool(String)
    /// Eine volle Spule aus dem Vorrat anbrechen.
    case newSpool
}

/// Ablauf "Einlagern" bzw. "In Drucker einlegen": erst das Filament (Musterplättchen) scannen,
/// dann wählen, welche Spule, dann den Container scannen bzw. Drucker und Kopf wählen.
@MainActor
@Observable
final class PlaceFlowModel {
    enum Step {
        case scanFilament
        case chooseSpool
        case chooseTarget
    }

    let target: PlaceTarget
    private(set) var step: Step = .scanFilament
    private(set) var item: Item?
    private(set) var choice: SpoolChoice?
    private(set) var container: Container?
    private(set) var printerPlace: SpoolPlace?
    private(set) var errorMessage: String?
    /// Ergebnis des letzten Vorgangs; bleibt stehen, bis das nächste Filament gescannt wird.
    private(set) var doneMessage: String?
    private(set) var isWorking = false

    /// Gewicht der Spule beim Einlegen: gewogen mit Spule und leere Spule ergeben den Rest, oder der Rest direkt.
    var weighed = ""
    var emptySpool = ""
    var remaining = ""

    init(target: PlaceTarget) {
        self.target = target
    }

    var title: String {
        target == .container ? "Einlagern" : "In Drucker einlegen"
    }

    var actionTitle: String {
        switch target {
        case .container: "Einlagern"
        case .printer: choice == .newSpool ? "Anbrechen und einlegen" : "Einlegen"
        }
    }

    /// Was von der gescannten Sorte bewegt werden kann.
    func choices(store: ContainerStore) -> [SpoolChoice] {
        guard let item else { return [] }
        let opened = store.spools(of: item.id).map { SpoolChoice.spool($0.id) }
        switch target {
        case .container:
            return [.stock] + opened
        case .printer:
            // Spulen pflegt nur das Backend
            guard store.backend != nil else { return [] }
            return opened + [.newSpool]
        }
    }

    // MARK: - Schritte

    func handleFilamentTag(_ tag: ScannedTag, store: ContainerStore) {
        let tagId = tag.tagId.trimmingCharacters(in: .whitespaces)
        guard !tagId.isEmpty else {
            errorMessage = "Ungültiger Tag."
            return
        }
        if let container = store.findContainer(byTag: tagId) {
            errorMessage = "Das ist der Container „\(container.name)“. Scanne zuerst das Filament."
            return
        }
        reset()
        doneMessage = nil
        item = existingOrNewItem(for: tag, store: store)
        errorMessage = tag.parseError

        let options = choices(store: store)
        if options.count == 1 {
            choose(options[0], store: store)
        } else {
            step = .chooseSpool
        }
    }

    func choose(_ choice: SpoolChoice, store: ContainerStore) {
        self.choice = choice
        step = .chooseTarget
        weighed = ""
        emptySpool = GramText.format(item?.spoolWeight)
        switch choice {
        case .spool(let id): remaining = GramText.format(store.spools.first { $0.id == id }?.remainingWeight)
        case .newSpool: remaining = GramText.format(item?.netWeight)
        case .stock: remaining = ""
        }
        // Steckt die Spule schon in einem Drucker, ist das der naheliegende Drucker
        if target == .printer, printerPlace == nil, case .spool(let id) = choice,
           let spool = store.spools.first(where: { $0.id == id }), spool.printerId != nil {
            printerPlace = spool.place
        }
    }

    /// Container-Tag im Schritt "Ziel". Im Druckerablauf wählt der Container eines Druckers diesen Drucker.
    func handleTargetTag(_ tag: ScannedTag, store: ContainerStore) {
        let tagId = tag.tagId.trimmingCharacters(in: .whitespaces)
        guard let found = store.findContainer(byTag: tagId) else {
            errorMessage = store.findItem(byTag: tagId) != nil
                ? "Das ist ein Filament-Tag. Scanne jetzt den Container."
                : "Container nicht gefunden. Lege ihn zuerst unter „Container“ an."
            return
        }
        switch target {
        case .container:
            selectContainer(found, store: store)
        case .printer:
            if let printer = store.printer(owningContainer: found.id) {
                selectPlace(.printer(printer.id, tool: 0))
            } else {
                errorMessage = "„\(found.name)“ ist kein Drucker. Wähle Drucker und Kopf aus der Liste."
            }
        }
    }

    func selectContainer(_ container: Container, store: ContainerStore) {
        if let printer = store.printer(owningContainer: container.id) {
            errorMessage = "Das ist der Container von „\(printer.name)“. Nimm dafür „In Drucker einlegen“."
            return
        }
        self.container = container
        errorMessage = nil
    }

    func selectPlace(_ place: SpoolPlace) {
        printerPlace = place
        errorMessage = nil
    }

    /// Neue Waage-Werte: der Rest folgt aus gewogen minus leerer Spule.
    func scaleChanged() {
        guard let gross = GramText.parse(weighed), let empty = GramText.parse(emptySpool) else { return }
        remaining = GramText.format(max(0, gross - empty))
    }

    /// Einzelne Spulen haben ein Gewicht, die Sorte mit ihrem Vorrat nicht.
    var asksForWeight: Bool {
        guard let choice else { return false }
        return choice != .stock
    }

    /// Eine angebrochene Spule bekommt ein anderes Restgewicht als bisher.
    func weightChanged(store: ContainerStore) -> Bool {
        guard case .spool(let id) = choice, let spool = store.spools.first(where: { $0.id == id }) else { return false }
        return GramText.parse(remaining) != spool.remainingWeight
    }

    // MARK: - Zustand

    /// Wo das Gewählte gerade ist, z. B. "U1 Kopf 2" oder "Regal oben".
    func currentPlaceText(store: ContainerStore) -> String? {
        guard let choice else { return nil }
        switch choice {
        case .stock:
            return item?.containerId.flatMap { store.container(id: $0)?.name } ?? "kein Container"
        case .spool(let id):
            return store.spools.first { $0.id == id }?.locationText(printers: store.printers, containers: store.containers)
        case .newSpool:
            return nil
        }
    }

    /// Das Gewählte ist schon am Ziel.
    func isAlreadyThere(store: ContainerStore) -> Bool {
        guard let choice else { return false }
        switch (target, choice) {
        case (.container, .stock):
            guard let container else { return false }
            return item?.containerId == container.id
        case (.container, .spool(let id)):
            guard let container, let spool = store.spools.first(where: { $0.id == id }) else { return false }
            return spool.place == .container(container.id)
        case (.printer, .spool(let id)):
            guard let printerPlace, let spool = store.spools.first(where: { $0.id == id }) else { return false }
            return spool.place == printerPlace
        default:
            return false
        }
    }

    /// Die Spule, die am gewählten Kopf steckt und dafür ins Lager wandert.
    func displacedSpool(store: ContainerStore) -> Spool? {
        guard target == .printer, let place = printerPlace, let printerId = place.printerId,
              let current = store.spool(inPrinter: printerId, tool: place.tool ?? 0) else { return nil }
        if case .spool(let id) = choice, id == current.id { return nil }
        return current
    }

    func canPerform(store: ContainerStore) -> Bool {
        guard step == .chooseTarget, choice != nil, !isWorking,
              !isAlreadyThere(store: store) || weightChanged(store: store) else { return false }
        switch target {
        case .container: return container != nil
        case .printer: return printerPlace?.printerId != nil
        }
    }

    // MARK: - Ausführen

    func perform(store: ContainerStore) async {
        guard canPerform(store: store), let item, let choice else { return }
        isWorking = true
        defer { isWorking = false }
        do {
            let message: String
            switch (target, choice) {
            case (.container, .stock):
                guard let container else { return }
                store.moveItem(id: item.id, toContainer: container.id)
                message = "\(item.displayTitle) liegt jetzt in „\(container.name)“."
            case (.container, .spool(let id)):
                guard let container, let spool = store.spools.first(where: { $0.id == id }) else { return }
                try await store.placeSpool(weighted(spool), at: .container(container.id))
                message = "Spule #\(spool.number) von \(item.displayTitle) liegt jetzt in „\(container.name)“."
            case (.printer, .spool(let id)):
                guard let place = printerPlace, let spool = store.spools.first(where: { $0.id == id }) else { return }
                try await store.placeSpool(weighted(spool), at: place)
                message = "Spule #\(spool.number) von \(item.displayTitle) steckt jetzt in \(place.text(printers: store.printers))."
            case (.printer, .newSpool):
                guard let place = printerPlace else { return }
                let spool = try await store.openSpool(itemId: item.id, remainingWeight: GramText.parse(remaining), place: place)
                let number = spool.map { " #\($0.number)" } ?? ""
                message = "Spule\(number) von \(item.displayTitle) angebrochen und in \(place.text(printers: store.printers)) eingelegt. Schreib die Nummer auf die Spule."
            default:
                return
            }
            reset()
            doneMessage = message
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func weighted(_ spool: Spool) -> Spool {
        var updated = spool
        if let grams = GramText.parse(remaining) { updated.remainingWeight = grams }
        return updated
    }

    func reset() {
        step = .scanFilament
        item = nil
        choice = nil
        container = nil
        printerPlace = nil
        errorMessage = nil
    }

    private func existingOrNewItem(for tag: ScannedTag, store: ContainerStore) -> Item {
        if let item = store.findItem(byTag: tag.tagId) { return item }
        let item = Item(
            rfidTag: tag.tagId,
            name: Item.defaultName(for: tag.itemData, fallback: "Filament"),
            itemData: tag.itemData
        )
        store.addItem(item)
        return item
    }
}
