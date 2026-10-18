import SwiftUI

/// Musterplättchen scannen und die gemessene Lichtdurchlässigkeit (TD) eintragen.
struct TransmissionScanView: View {
    @Environment(ContainerStore.self) private var store
    @State private var itemId: String?
    @State private var value = ""
    @State private var errorMessage: String?
    @State private var savedMessage: String?
    @FocusState private var fieldFocused: Bool

    var body: some View {
        Form {
            Section {
                ScanButton(title: itemId == nil ? "Musterplättchen scannen" : "Nächstes scannen",
                           prompt: "Halten Sie das iPhone an den Filament-Tag.") { tag in
                    handle(tag)
                } onError: { errorMessage = $0 }
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
            } footer: {
                Text("TD (Transmission Distance, HueForge) in mm, wie ihn z. B. ein TD-1 anzeigt. Je höher, desto mehr Licht lässt das Filament durch.")
            }

            if let savedMessage {
                Section {
                    Label(savedMessage, systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                }
            }

            if let itemId, let item = store.item(id: itemId) {
                Section {
                    ItemHeader(item: item, detail: item.transmissionText.map { "Bisher: \($0)" } ?? "Noch nicht gemessen")
                    TransmissionField(text: $value)
                        .focused($fieldFocused)
                    Button("Speichern") { save(item) }
                        .disabled(GramText.parse(value) == nil)
                }
            }

            if let errorMessage {
                Section {
                    Text(errorMessage).foregroundStyle(.red)
                }
            }
        }
        .navigationTitle("Lichtdurchlässigkeit")
    }

    private func handle(_ tag: ScannedTag) {
        let tagId = tag.tagId.trimmingCharacters(in: .whitespaces)
        savedMessage = nil
        if let container = store.findContainer(byTag: tagId) {
            errorMessage = "Das ist der Container „\(container.name)“. Scanne das Musterplättchen."
            return
        }
        let item: Item
        if let existing = store.findItem(byTag: tagId) {
            item = existing
        } else {
            item = Item(rfidTag: tagId, name: Item.defaultName(for: tag.itemData, fallback: "Filament"), itemData: tag.itemData)
            store.addItem(item)
        }
        itemId = item.id
        value = GramText.format(item.transmissionDistance)
        errorMessage = nil
        fieldFocused = true
    }

    private func save(_ item: Item) {
        var updated = item
        let parsed = GramText.parse(value)
        if let parsed, parsed < 0 {
            errorMessage = "Die Lichtdurchlässigkeit darf nicht negativ sein."
            return
        }
        updated.transmissionDistance = parsed
        store.updateItem(updated)
        savedMessage = "\(item.displayTitle): TD \(updated.transmissionText ?? "")"
        itemId = nil
        value = ""
        fieldFocused = false
    }
}
