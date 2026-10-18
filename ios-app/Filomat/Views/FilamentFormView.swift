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
    @State private var spoolWeight: String
    @State private var spoolsInStock: Int
    @State private var isOpeningSpool = false
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
        _spoolWeight = State(initialValue: Self.gramText(item?.spoolWeight))
        _spoolsInStock = State(initialValue: item?.spoolsInStock ?? 0)
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

                if let item = editedItem {
                    Section {
                        GramField(title: "Füllgewicht neue Spule (g)", text: $netWeight)
                        GramField(title: "Leere Spule (g)", text: $spoolWeight)
                        Stepper("Volle Spulen im Vorrat: \(spoolsInStock)", value: $spoolsInStock, in: 0...99)
                    } header: {
                        Text("Sorte")
                    } footer: {
                        Text("Das Leergewicht kommt, falls bekannt, aus der SpoolmanDB für die Marke. Reicht eine Spule für einen Druck nicht, wird automatisch eine aus dem Vorrat angebrochen.")
                    }

                    if store.backend != nil {
                        Section {
                            ForEach(store.spools(of: item.id)) { spool in
                                NavigationLink {
                                    SpoolEditView(spool: spool, emptySpoolWeight: Self.grams(spoolWeight))
                                } label: {
                                    SpoolRow(spool: spool, location: spool.locationText(printers: store.printers))
                                }
                            }
                            Button {
                                Task { await openSpool(item) }
                            } label: {
                                HStack {
                                    Text("Neue Spule anbrechen")
                                    Spacer()
                                    if isOpeningSpool { ProgressView() }
                                }
                            }
                            .disabled(isOpeningSpool)
                        } header: {
                            Text("Angebrochene Spulen")
                        } footer: {
                            Text("Schreib die Nummer mit Stift auf die Spule. Gedruckter Verbrauch wird von der Spule abgezogen, die im Drucker an diesem Kopf steckt.")
                        }
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
            .task {
                await loadConsumption()
                await suggestSpoolWeight()
            }
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
        item.spoolWeight = Self.grams(spoolWeight)
        item.spoolsInStock = spoolsInStock
    }

    /// Bricht eine volle Spule an (Platz: Lager). Gewicht und Platz werden danach an der Spule gesetzt.
    private func openSpool(_ item: Item) async {
        isOpeningSpool = true
        defer { isOpeningSpool = false }
        do {
            // Eingetragene Werte zuerst speichern (abwarten, damit der Vorrat danach nicht überschrieben wird)
            var updated = item
            updated.itemData = filamentData
            applyWeights(to: &updated)
            try await store.backend?.importData(BackendSnapshot(containers: [], items: [updated]))
            try await store.openSpool(itemId: item.id)
            if let refreshed = store.items.first(where: { $0.id == item.id }) {
                spoolsInStock = refreshed.spoolsInStock ?? 0
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Leergewicht der Marke aus der SpoolmanDB vorschlagen, wenn noch keins eingetragen ist.
    private func suggestSpoolWeight() async {
        let brandName = brand.trimmingCharacters(in: .whitespaces).lowercased()
        guard editedItem != nil, spoolWeight.isEmpty, !brandName.isEmpty, let backend = store.backend,
              let weights = try? await backend.spoolWeights(),
              let match = weights.first(where: { $0.key.lowercased() == brandName })?.value
                ?? weights.first(where: { $0.key.lowercased().hasPrefix(brandName) })?.value,
              spoolWeight.isEmpty else { return }
        spoolWeight = Self.gramText(match)
    }

    private static func grams(_ text: String) -> Double? { GramText.parse(text) }

    private static func gramText(_ value: Double?) -> String { GramText.format(value) }

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

private struct SpoolRow: View {
    var spool: Spool
    var location: String

    var body: some View {
        HStack {
            Text("#\(spool.number)").font(.headline).monospacedDigit()
            Text(location).foregroundStyle(.secondary)
            Spacer()
            if let grams = spool.remainingWeight {
                Text("\(Int(grams.rounded())) g").monospacedDigit()
            }
        }
    }
}

/// Angebrochene Spule wiegen, einlegen oder als leer markieren.
private struct SpoolEditView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss

    let spool: Spool
    @State private var remaining: String
    @State private var weighed = ""
    @State private var emptySpool: String
    @State private var location: Location
    @State private var isSaving = false
    @State private var errorMessage: String?

    /// Platz einer Spule: Lager oder ein Kopf eines Druckers.
    struct Location: Hashable {
        var printerId: String?
        var tool: Int?
    }

    init(spool: Spool, emptySpoolWeight: Double?) {
        self.spool = spool
        _remaining = State(initialValue: GramText.format(spool.remainingWeight))
        _emptySpool = State(initialValue: GramText.format(emptySpoolWeight))
        _location = State(initialValue: Location(printerId: spool.printerId, tool: spool.printerId == nil ? nil : spool.tool ?? 0))
    }

    var body: some View {
        Form {
            Section {
                GramField(title: "Gewogen mit Spule (g)", text: $weighed)
                GramField(title: "Leere Spule (g)", text: $emptySpool)
                GramField(title: "Restgewicht (g)", text: $remaining)
            } header: {
                Text("Wiegen")
            } footer: {
                Text("Das Restgewicht wird aus dem gewogenen Gewicht minus leerer Spule berechnet.")
            }

            Section("Platz") {
                Picker("Steckt in", selection: $location) {
                    Text("Lager").tag(Location(printerId: nil, tool: nil))
                    ForEach(store.printers) { printer in
                        ForEach(0..<printer.toolCount, id: \.self) { tool in
                            Text(printer.toolCount > 1 ? "\(printer.name) Kopf \(tool + 1)" : printer.name)
                                .tag(Location(printerId: printer.id, tool: tool))
                        }
                    }
                }
            }

            Section {
                Button("Speichern") { Task { await save() } }
                    .disabled(isSaving)
                Button("Spule ist leer", role: .destructive) { Task { await markEmpty() } }
                    .disabled(isSaving)
            } footer: {
                if let errorMessage { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .navigationTitle("Spule #\(spool.number)")
        .onChange(of: weighed) { updateFromScale() }
        .onChange(of: emptySpool) { updateFromScale() }
    }

    private func updateFromScale() {
        guard let gross = GramText.parse(weighed), let empty = GramText.parse(emptySpool) else { return }
        remaining = GramText.format(max(0, gross - empty))
    }

    private func save() async {
        isSaving = true
        defer { isSaving = false }
        var updated = spool
        updated.remainingWeight = GramText.parse(remaining)
        updated.printerId = location.printerId
        updated.tool = location.tool
        do {
            try await store.updateSpool(updated)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func markEmpty() async {
        isSaving = true
        defer { isSaving = false }
        do {
            try await store.markSpoolEmpty(id: spool.id)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

/// Gramm-Angaben als Text (Komma oder Punkt als Dezimaltrenner).
private enum GramText {
    static func parse(_ text: String) -> Double? {
        Double(text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "."))
    }

    static func format(_ value: Double?) -> String {
        guard let value else { return "" }
        return value.rounded() == value ? String(Int(value)) : String(value)
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
