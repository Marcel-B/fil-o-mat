import SwiftUI

/// Neuer Container: Daten erfassen, auf ein Tag schreiben, Container mit der UID des Tags speichern.
struct AddContainerView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss

    @State private var name = ""
    @State private var description = ""
    @State private var capacityText = ""
    @State private var isWriting = false
    @State private var errorMessage: String?

    private var capacity: Int? { Int(capacityText.trimmingCharacters(in: .whitespaces)) }
    private var capacityInvalid: Bool { !capacityText.trimmingCharacters(in: .whitespaces).isEmpty && capacity == nil }
    private var canContinue: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty && !capacityInvalid && NFCService.isAvailable && !isWriting
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name *", text: $name)
                    TextField("Beschreibung", text: $description, axis: .vertical)
                    TextField("Menge (optional)", text: $capacityText)
                        .keyboardType(.numberPad)
                } footer: {
                    if capacityInvalid {
                        Text("Bitte eine gültige Zahl eingeben").foregroundStyle(.red)
                    }
                }

                Section {
                    Button {
                        Task { await writeTag() }
                    } label: {
                        HStack {
                            Text("Tag beschreiben")
                            Spacer()
                            if isWriting { ProgressView() }
                        }
                    }
                    .disabled(!canContinue)
                } footer: {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Die Container-Daten werden auf das RFID-Tag geschrieben, danach wird der Container gespeichert.")
                        if !NFCService.isAvailable {
                            Text("NFC ist auf diesem Gerät nicht verfügbar.").foregroundStyle(.red)
                        }
                        if let errorMessage {
                            Text(errorMessage).foregroundStyle(.red)
                        }
                    }
                }
            }
            .disabled(isWriting)
            .navigationTitle("Neuer Container")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
        }
    }

    private func writeTag() async {
        isWriting = true
        errorMessage = nil
        defer { isWriting = false }

        let tagData = ContainerTagData(
            name: name,
            description: description,
            capacity: capacity.map(String.init) ?? ""
        )
        let json: Data
        do {
            json = try TagJSON.encoder.encode(tagData)
        } catch {
            errorMessage = String(localized: "Fehler beim Erstellen der JSON-Daten: \(error.localizedDescription)")
            return
        }

        do {
            let tagId = try await NFCService.shared.writeJSON(
                json,
                prompt: String(localized: "Halten Sie das iPhone an das Tag des Containers.")
            ) { [store] tagId in
                if let existing = store.findContainer(byTag: tagId) {
                    return String(localized: "Dieses Tag gehört bereits zum Container '\(existing.name)'.")
                }
                if store.findItem(byTag: tagId) != nil {
                    return String(localized: "Dieses Tag gehört bereits zu einem Filament.")
                }
                return nil
            }
            store.addContainer(Container(
                rfidTag: tagId,
                name: name,
                description: description,
                capacity: capacity
            ))
            dismiss()
        } catch NFCError.cancelled {
            // Nutzer hat das Scanfenster geschlossen
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct EditContainerView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss

    let container: Container
    @State private var name: String
    @State private var description: String

    init(container: Container) {
        self.container = container
        _name = State(initialValue: container.name)
        _description = State(initialValue: container.description)
    }

    var body: some View {
        NavigationStack {
            Form {
                TextField("Name", text: $name)
                TextField("Beschreibung", text: $description, axis: .vertical)
                LabeledContent("RFID-Tag", value: container.rfidTag)
            }
            .navigationTitle("Container bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Speichern") {
                        var updated = container
                        updated.name = name
                        updated.description = description
                        store.updateContainer(updated)
                        dismiss()
                    }
                    .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
    }
}

/// JSON-Kodierung für Tag-Inhalte (kompakt, ohne escapte Slashes).
enum TagJSON {
    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.withoutEscapingSlashes]
        return encoder
    }()
}
