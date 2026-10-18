import Foundation
import Observation

enum ScanMode: String, CaseIterable, Identifiable {
    case addToContainer
    case removeFromContainer

    var id: Self { self }

    var title: String {
        switch self {
        case .addToContainer: return "Einlagern"
        case .removeFromContainer: return "Herausnehmen"
        }
    }
}

/// Ablauf "Einlagern" (Container → Filament) bzw. "Herausnehmen" (Filament → Container).
@MainActor
@Observable
final class ScanViewModel {
    private(set) var scanMode: ScanMode = .addToContainer
    private(set) var scannedContainer: Container?
    private(set) var scannedItem: Item?
    private(set) var currentStep = 0
    private(set) var errorMessage: String?
    private(set) var warningMessage: String?
    var showConfirmMove = false

    var stepTitle: String {
        switch (scanMode, currentStep) {
        case (.addToContainer, 0): return "Schritt 1: Container scannen"
        case (.addToContainer, _): return "Schritt 2: Filament scannen"
        case (.removeFromContainer, 0): return "Schritt 1: Filament scannen"
        case (.removeFromContainer, _): return "Schritt 2: Container scannen"
        }
    }

    var stepDescription: String {
        switch (scanMode, currentStep) {
        case (.addToContainer, 0):
            return "Scannen Sie zuerst den Container, in den Sie das Filament einlagern möchten."
        case (.addToContainer, _):
            return "Scannen Sie nun das Filament, das Sie in den Container einlagern möchten."
        case (.removeFromContainer, 0):
            return "Scannen Sie zuerst das Filament, das Sie aus dem Container herausnehmen möchten."
        case (.removeFromContainer, _):
            return "Scannen Sie nun den Container, aus dem Sie das Filament herausnehmen möchten."
        }
    }

    var scanPrompt: String {
        let scansContainer = (scanMode == .addToContainer) == (currentStep == 0)
        return scansContainer ? "Halten Sie das iPhone an den Container-Tag." : "Halten Sie das iPhone an den Filament-Tag."
    }

    var canPerformAction: Bool {
        scannedContainer != nil && scannedItem != nil && warningMessage == nil && !showConfirmMove
    }

    var actionTitle: String {
        scanMode == .addToContainer ? "Filament einlagern" : "Filament herausnehmen"
    }

    func setScanMode(_ mode: ScanMode) {
        scanMode = mode
        reset()
    }

    func scan(using nfc: NFCService, store: ContainerStore) async {
        do {
            let tag = try await nfc.scan(prompt: scanPrompt)
            handleScannedTag(tag, store: store)
            if let parseError = tag.parseError { errorMessage = parseError }
        } catch NFCError.cancelled {
            // Nutzer hat das Scanfenster geschlossen
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func handleScannedTag(_ tag: ScannedTag, store: ContainerStore) {
        let scansContainer = (scanMode == .addToContainer) == (currentStep == 0)
        if scansContainer {
            if let container = store.findContainer(byTag: tag.tagId) {
                scannedContainer = container
                errorMessage = nil
                if scanMode == .addToContainer { currentStep = 1 }
            } else {
                errorMessage = "Container nicht gefunden. Bitte erstellen Sie den Container zuerst."
                scannedContainer = nil
            }
            return
        }

        let item = existingOrNewItem(for: tag, store: store)
        if scanMode == .removeFromContainer {
            scannedItem = item
            currentStep = 1
            return
        }

        if let containerId = item.containerId {
            if containerId == scannedContainer?.id {
                warningMessage = "Dieses Item ist bereits in diesem Container."
                scannedItem = nil
            } else {
                scannedItem = item
                showConfirmMove = true
            }
        } else {
            scannedItem = item
            warningMessage = nil
        }
    }

    func performAction(store: ContainerStore) {
        if let container = scannedContainer, let item = scannedItem {
            switch scanMode {
            case .addToContainer:
                store.moveItem(id: item.id, toContainer: container.id)
            case .removeFromContainer:
                store.removeItemFromContainer(id: item.id)
            }
        }
        reset()
    }

    func confirmMove() {
        showConfirmMove = false
    }

    func cancelMove() {
        showConfirmMove = false
        scannedItem = nil
        warningMessage = nil
    }

    func reset() {
        scannedContainer = nil
        scannedItem = nil
        currentStep = 0
        errorMessage = nil
        warningMessage = nil
        showConfirmMove = false
    }

    private func existingOrNewItem(for tag: ScannedTag, store: ContainerStore) -> Item {
        if let item = store.findItem(byTag: tag.tagId) { return item }
        let item = Item(
            rfidTag: tag.tagId,
            name: Item.defaultName(for: tag.itemData, fallback: "Item"),
            itemData: tag.itemData
        )
        store.addItem(item)
        return item
    }
}
