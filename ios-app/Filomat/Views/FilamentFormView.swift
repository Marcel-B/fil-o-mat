import SwiftUI

/// Filament anlegen (Daten auf ein Tag schreiben) oder bearbeiten (speichern, optional auch auf das Tag).
struct FilamentFormView: View {
    enum Mode {
        case create
        case edit(Item)
    }

    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss

    let mode: Mode
    private let presets = FilamentPresetDatabase.shared

    @State private var type: String
    @State private var brand: String
    @State private var variant: String
    @State private var colorHex: String
    @State private var color: String
    @State private var minTemp: String
    @State private var maxTemp: String
    @State private var bedMinTemp: String
    @State private var bedMaxTemp: String
    @State private var netWeight: String
    @State private var remainingWeight: String
    @State private var isWriting = false
    @State private var consumption: [ConsumptionEntry] = []
    @State private var errorMessage: String?

    init(mode: Mode) {
        self.mode = mode
        var data: ItemJsonData?
        var item: Item?
        if case .edit(let edited) = mode {
            item = edited
            data = edited.itemData
        }
        _netWeight = State(initialValue: Self.gramText(item?.netWeight))
        _remainingWeight = State(initialValue: Self.gramText(item?.remainingWeight))
        _type = State(initialValue: data?.type ?? "")
        _brand = State(initialValue: data?.brand ?? "")
        _variant = State(initialValue: data?.variant ?? "")
        _colorHex = State(initialValue: data?.colorHex ?? "")
        _color = State(initialValue: data?.color ?? "")
        _minTemp = State(initialValue: data?.minTemp ?? "")
        _maxTemp = State(initialValue: data?.maxTemp ?? "")
        _bedMinTemp = State(initialValue: data?.bedMinTemp ?? "")
        _bedMaxTemp = State(initialValue: data?.bedMaxTemp ?? "")
    }

    private var editedItem: Item? {
        if case .edit(let item) = mode { return item }
        return nil
    }

    private var typeOptions: [String] {
        // Ein bestehender, unbekannter Typ bleibt auswählbar
        FilamentPresetDatabase.filamentTypes.contains(type) || type.isEmpty
            ? FilamentPresetDatabase.filamentTypes
            : FilamentPresetDatabase.filamentTypes + [type]
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Grunddaten") {
                    Picker("Typ *", selection: $type) {
                        if type.isEmpty { Text("Bitte wählen").tag("") }
                        ForEach(typeOptions, id: \.self) { Text($0).tag($0) }
                    }
                    SuggestionField(title: "Marke", text: $brand, options: presets.brands)
                    SuggestionField(title: "Variante", text: $variant, options: presets.variants)
                    HStack {
                        TextField("Farbe (Hex), z. B. #FF0000", text: $colorHex)
                            .textInputAutocapitalization(.characters)
                            .autocorrectionDisabled()
                        ColorDot(hex: colorHex, size: 32)
                    }
                    TextField("Farbe (Text), z. B. Rot", text: $color)
                }

                Section("Temperaturen") {
                    TemperatureField(title: "Min. Temperatur (°C)", text: $minTemp)
                    TemperatureField(title: "Max. Temperatur (°C)", text: $maxTemp)
                    TemperatureField(title: "Min. Bett-Temperatur (°C)", text: $bedMinTemp)
                    TemperatureField(title: "Max. Bett-Temperatur (°C)", text: $bedMaxTemp)
                }

                if editedItem != nil {
                    Section {
                        GramField(title: "Füllgewicht neue Spule (g)", text: $netWeight)
                        GramField(title: "Restgewicht (g)", text: $remainingWeight)
                    } header: {
                        Text("Gewicht")
                    } footer: {
                        Text("Gedruckter Verbrauch wird automatisch vom Restgewicht abgezogen. Ohne Restgewicht wird vom Füllgewicht ausgegangen.")
                    }
                }

                if !consumption.isEmpty {
                    Section("Verbrauch") {
                        ForEach(consumption) { entry in
                            HStack {
                                VStack(alignment: .leading) {
                                    Text(entry.jobName).lineLimit(1)
                                    if let date = entry.endedDate {
                                        Text(date.formatted(date: .abbreviated, time: .shortened))
                                            .font(.caption)
                                            .foregroundStyle(.secondary)
                                    }
                                }
                                Spacer()
                                Text(String(format: "%.1f g", entry.grams)).monospacedDigit()
                            }
                        }
                    }
                }

                Section {
                    if editedItem != nil {
                        Button("Speichern") { save() }
                            .disabled(type.isEmpty || isWriting)
                    }
                    Button {
                        Task { await writeTag() }
                    } label: {
                        HStack {
                            Text(editedItem == nil ? "Auf Tag schreiben" : "Speichern & auf Tag schreiben")
                            Spacer()
                            if isWriting { ProgressView() }
                        }
                    }
                    .disabled(type.isEmpty || isWriting || !NFCService.isAvailable)
                } footer: {
                    VStack(alignment: .leading, spacing: 4) {
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
            .navigationTitle(editedItem == nil ? "Neues Filament" : "Filament bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
            }
            .task { await loadConsumption() }
            .onChange(of: type) { loadTemperatures() }
            .onChange(of: brand) { loadTemperatures() }
        }
    }

    private func loadConsumption() async {
        guard let item = editedItem, let backend = store.backend else { return }
        consumption = (try? await backend.consumption(itemId: item.id)) ?? []
    }

    private func applyWeights(to item: inout Item) {
        item.netWeight = Self.grams(netWeight)
        item.remainingWeight = Self.grams(remainingWeight)
    }

    private static func grams(_ text: String) -> Double? {
        Double(text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "."))
    }

    private static func gramText(_ value: Double?) -> String {
        guard let value else { return "" }
        return value.rounded() == value ? String(Int(value)) : String(value)
    }

    /// Temperaturen aus den Presets übernehmen. Beim Bearbeiten werden nur leere Felder gefüllt.
    private func loadTemperatures() {
        guard !type.isEmpty,
              let preset = presets.preset(type: type, brand: brand.trimmingCharacters(in: .whitespaces)) else { return }
        let overwrite = editedItem == nil
        if overwrite || minTemp.isEmpty { minTemp = preset.minTemp }
        if overwrite || maxTemp.isEmpty { maxTemp = preset.maxTemp }
        if overwrite || bedMinTemp.isEmpty { bedMinTemp = preset.bedMinTemp ?? "" }
        if overwrite || bedMaxTemp.isEmpty { bedMaxTemp = preset.bedMaxTemp ?? "" }
    }

    private var filamentData: ItemJsonData {
        func value(_ s: String) -> String? {
            let trimmed = s.trimmingCharacters(in: .whitespaces)
            return trimmed.isEmpty ? nil : trimmed
        }
        return ItemJsonData(
            protocolName: editedItem?.itemData?.protocolName ?? "openspool",
            version: editedItem?.itemData?.version ?? "1.0",
            type: value(type),
            colorHex: HexColor.stored(colorHex),
            brand: value(brand),
            minTemp: value(minTemp),
            maxTemp: value(maxTemp),
            bedMinTemp: value(bedMinTemp),
            bedMaxTemp: value(bedMaxTemp),
            variant: value(variant),
            color: value(color)
        )
    }

    private func save() {
        guard var item = editedItem else { return }
        item.itemData = filamentData
        applyWeights(to: &item)
        store.updateItem(item, sendWeights: true)
        dismiss()
    }

    private func writeTag() async {
        isWriting = true
        errorMessage = nil
        defer { isWriting = false }

        let data = filamentData
        let json: Data
        do {
            json = try TagJSON.encoder.encode(data)
        } catch {
            errorMessage = "Fehler beim Erstellen der Daten: \(error.localizedDescription)"
            return
        }

        let expectedTag = editedItem?.rfidTag
        do {
            _ = try await NFCService.shared.writeJSON(
                json,
                prompt: "Halten Sie das iPhone an den Filament-Tag."
            ) { [store] tagId in
                if let expectedTag, tagId != expectedTag {
                    return "Das ist nicht der Tag dieses Filaments."
                }
                if let container = store.findContainer(byTag: tagId) {
                    return "Dieses Tag gehört zum Container '\(container.name)'."
                }
                return nil
            }
            if var item = editedItem {
                item.itemData = data
                applyWeights(to: &item)
                store.updateItem(item, sendWeights: true)
            }
            dismiss()
        } catch NFCError.cancelled {
            // Nutzer hat das Scanfenster geschlossen
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

/// Textfeld mit Vorschlagsliste (entspricht dem Autocomplete-Dropdown der Android-App).
private struct SuggestionField: View {
    var title: String
    @Binding var text: String
    var options: [String]

    private var filtered: [String] {
        let query = text.trimmingCharacters(in: .whitespaces)
        return query.isEmpty ? options : options.filter { $0.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        HStack {
            TextField(title, text: $text)
                .autocorrectionDisabled()
            if !filtered.isEmpty {
                Menu {
                    ForEach(filtered, id: \.self) { option in
                        Button(option) { text = option }
                    }
                } label: {
                    Image(systemName: "chevron.down.circle")
                }
                .accessibilityLabel("\(title) auswählen")
            }
        }
    }
}

private struct TemperatureField: View {
    var title: String
    @Binding var text: String

    var body: some View {
        LabeledContent(title) {
            TextField("°C", text: $text)
                .keyboardType(.numberPad)
                .multilineTextAlignment(.trailing)
                .frame(maxWidth: 80)
                .onChange(of: text) { _, newValue in
                    let digits = newValue.filter { $0.isASCII && $0.isNumber }
                    if digits != newValue { text = digits }
                }
        }
    }
}

/// Eingabefeld für Gramm.
private struct GramField: View {
    var title: String
    @Binding var text: String

    var body: some View {
        LabeledContent(title) {
            TextField("z. B. 1000", text: $text)
                .keyboardType(.decimalPad)
                .multilineTextAlignment(.trailing)
        }
    }
}
