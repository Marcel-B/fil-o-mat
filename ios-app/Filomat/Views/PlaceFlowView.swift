import SwiftUI

/// "Einlagern" bzw. "In Drucker einlegen" in drei Schritten: Filament, Spule, Ziel.
struct PlaceFlowView: View {
    @Environment(ContainerStore.self) private var store
    @State private var model: PlaceFlowModel

    init(target: PlaceTarget) {
        _model = State(initialValue: PlaceFlowModel(target: target))
    }

    var body: some View {
        List {
            if let done = model.doneMessage {
                Section {
                    Label(done, systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                }
            }

            filamentSection

            if model.step != .scanFilament {
                choiceSection
            }
            if model.step == .chooseTarget {
                switch model.target {
                case .container: containerSection
                case .printer: printerSection
                }
            }

            if let error = model.errorMessage {
                Section {
                    Text(error).foregroundStyle(.red)
                }
            }
        }
        .navigationTitle(model.title)
        .toolbar {
            if model.step != .scanFilament {
                Button("Neu beginnen") { model.reset() }
            }
        }
        .safeAreaInset(edge: .bottom) {
            if model.step == .chooseTarget {
                Button {
                    Task { await model.perform(store: store) }
                } label: {
                    HStack {
                        if model.isWorking { ProgressView() }
                        Text(model.actionTitle)
                    }
                    .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(!model.canPerform(store: store))
                .padding()
                .background(.bar)
            }
        }
    }

    // MARK: Schritt 1: Filament

    private var filamentSection: some View {
        Section("1. Filament") {
            if let item = model.item {
                ItemHeader(item: item, detail: stockText(for: item))
            } else {
                Text(model.target == .container
                     ? "Scanne das Musterplättchen des Filaments, das eingelagert wird."
                     : "Scanne das Musterplättchen des Filaments, das in den Drucker kommt.")
                    .foregroundStyle(.secondary)
                ScanButton(title: "Filament scannen", prompt: "Halten Sie das iPhone an den Filament-Tag.") { tag in
                    model.handleFilamentTag(tag, store: store)
                }
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
            }
        }
    }

    // MARK: Schritt 2: welche Spule

    private var choiceSection: some View {
        Section {
            let options = model.choices(store: store)
            if options.isEmpty {
                Text("Für Spulen in Druckern muss das Backend verbunden sein.").foregroundStyle(.secondary)
            }
            ForEach(options, id: \.self) { choice in
                Button {
                    model.choose(choice, store: store)
                } label: {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(title(of: choice)).foregroundStyle(.primary)
                            Text(detail(of: choice)).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        if model.choice == choice {
                            Image(systemName: "checkmark").foregroundStyle(.tint)
                        }
                    }
                }
            }
        } header: {
            Text("2. Was kommt \(model.target == .container ? "in den Container" : "in den Drucker")?")
        }
    }

    private func title(of choice: SpoolChoice) -> String {
        switch choice {
        case .stock: return "Sorte mit vollen Spulen"
        case .spool(let id): return "Angebrochene Spule #\(spool(id)?.number ?? 0)"
        case .newSpool: return "Neue Spule anbrechen"
        }
    }

    private func detail(of choice: SpoolChoice) -> String {
        switch choice {
        case .stock:
            let place = model.item?.containerId.flatMap { store.container(id: $0)?.name } ?? "kein Container"
            return "Musterplättchen und Vorrat · bisher: \(place)"
        case .spool(let id):
            guard let spool = spool(id) else { return "" }
            let rest = spool.remainingWeight.map { "\(Int($0.rounded())) g" } ?? "Rest unbekannt"
            return "\(rest) · \(spool.locationText(printers: store.printers, containers: store.containers))"
        case .newSpool:
            let stock = model.item?.spoolsInStock ?? 0
            return stock > 0
                ? "Aus dem Vorrat (\(stock) \(stock == 1 ? "Spule" : "Spulen"))"
                : "Im Vorrat ist keine Spule mehr eingetragen"
        }
    }

    // MARK: Schritt 3: Ziel

    private var containerSection: some View {
        Section("3. Container") {
            if let container = model.container {
                LabeledContent("Container", value: container.name)
                if model.isAlreadyThere(store: store) {
                    Text("Liegt schon in diesem Container.").foregroundStyle(.orange)
                }
            }
            ScanButton(title: model.container == nil ? "Container scannen" : "Anderen Container scannen",
                       prompt: "Halten Sie das iPhone an den Container-Tag.") { tag in
                model.handleTargetTag(tag, store: store)
            }
            .listRowInsets(EdgeInsets())
            .listRowBackground(Color.clear)
            Menu("Aus der Liste wählen") {
                ForEach(store.storageContainers) { container in
                    Button(container.name) { model.selectContainer(container, store: store) }
                }
            }
        }
    }

    private var printerSection: some View {
        Section {
            if store.printers.isEmpty {
                Text("Noch keine Drucker angelegt (Tab „Drucker“).").foregroundStyle(.secondary)
            }
            ForEach(store.printers) { printer in
                ForEach(0..<printer.toolCount, id: \.self) { tool in
                    let place = SpoolPlace.printer(printer.id, tool: tool)
                    Button {
                        model.selectPlace(place)
                    } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(printer.placeName(tool: tool)).foregroundStyle(.primary)
                                Text(occupant(printerId: printer.id, tool: tool))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            if model.printerPlace == place {
                                Image(systemName: "checkmark").foregroundStyle(.tint)
                            }
                        }
                    }
                }
            }
        } header: {
            Text("3. Drucker und Kopf")
        } footer: {
            if let displaced = model.displacedSpool(store: store) {
                Text("Dort steckt gerade \(name(of: displaced)); sie kommt ins Lager.")
            } else if model.isAlreadyThere(store: store) {
                Text("Die Spule steckt schon dort.")
            }
        }
    }

    // MARK: Hilfen

    private func spool(_ id: String) -> Spool? {
        store.spools.first { $0.id == id }
    }

    private func occupant(printerId: String, tool: Int) -> String {
        guard let spool = store.spool(inPrinter: printerId, tool: tool) else { return "frei" }
        return name(of: spool)
    }

    private func name(of spool: Spool) -> String {
        let title = store.item(id: spool.itemId)?.displayTitle ?? "Filament"
        return "\(title) #\(spool.number)"
    }

    private func stockText(for item: Item) -> String {
        var parts: [String] = []
        if let stock = item.spoolsInStock { parts.append("Vorrat: \(stock)") }
        let opened = store.spools(of: item.id).count
        if opened > 0 { parts.append("\(opened) angebrochen") }
        return parts.isEmpty ? item.rfidTag : parts.joined(separator: " · ")
    }
}
