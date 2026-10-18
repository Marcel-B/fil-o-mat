import SwiftUI

/// Liest einen beliebigen Tag und listet alles auf, was darauf steht und was Fil-o-mat dazu weiß.
struct TagInfoView: View {
    @Environment(ContainerStore.self) private var store
    @State private var tag: ScannedTag?
    @State private var errorMessage: String?
    @State private var isScanning = false

    var body: some View {
        List {
            Section {
                if NFCService.isAvailable {
                    Button {
                        Task { await scan() }
                    } label: {
                        HStack {
                            if isScanning { ProgressView() }
                            Text(isScanning ? "Scanne..." : (tag == nil ? "Tag lesen" : "Nächsten Tag lesen"))
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(isScanning)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
                } else {
                    Text("NFC ist auf diesem Gerät nicht verfügbar.").foregroundStyle(.red)
                }
                if let errorMessage {
                    Text(errorMessage).foregroundStyle(.red)
                }
            }

            if let tag {
                knownSection(for: tag)
                filamentSection(for: tag)
                chipSection(for: tag)
                recordsSection(for: tag)
            }
        }
        .navigationTitle("Tag lesen")
    }

    // MARK: Abschnitte

    @ViewBuilder
    private func knownSection(for tag: ScannedTag) -> some View {
        let uid = tag.details?.uid ?? tag.tagId
        if let container = store.findContainer(byTag: uid) ?? store.findContainer(byTag: tag.tagId) {
            Section("In Fil-o-mat") {
                InfoRow(label: "Container", value: container.name)
                if !container.description.isEmpty {
                    Text(container.description).foregroundStyle(.secondary)
                }
                InfoRow(label: "Filamente darin", value: "\(store.items(in: container.id).count)")
            }
        } else if let item = store.findItem(byTag: uid) ?? store.findItem(byTag: tag.tagId) {
            Section("In Fil-o-mat") {
                HStack(spacing: 12) {
                    ColorDot(hex: item.itemData?.colorHex, size: 32, showsPlaceholder: false)
                    Text(item.displayTitle).font(.headline)
                }
                if item.name != item.displayTitle { InfoRow(label: "Name", value: item.name) }
                if !item.description.isEmpty {
                    Text(item.description).foregroundStyle(.secondary)
                }
                InfoRow(
                    label: "Container",
                    value: item.containerId.flatMap { store.container(id: $0)?.name } ?? "keiner"
                )
                if let stock = item.spoolsInStock { InfoRow(label: "Volle Spulen im Vorrat", value: "\(stock)") }
                if let net = item.netWeight { InfoRow(label: "Filament pro Spule", value: grams(net)) }
                if let spool = item.spoolWeight { InfoRow(label: "Leere Spule", value: grams(spool)) }
                let spools = store.spools(of: item.id)
                if spools.isEmpty {
                    InfoRow(label: "Angebrochene Spulen", value: "keine")
                } else {
                    ForEach(spools) { spool in
                        LabeledContent("Spule \(spool.number)") {
                            VStack(alignment: .trailing) {
                                Text(spool.remainingWeight.map(grams) ?? "Rest unbekannt")
                                Text(spool.locationText(printers: store.printers))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        }
                    }
                }
            }
        } else {
            Section("In Fil-o-mat") {
                Text("Dieser Tag ist in Fil-o-mat noch nicht bekannt.").foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private func filamentSection(for tag: ScannedTag) -> some View {
        if let record = tag.details?.records.first(where: \.isJSON) {
            Section("Filament-Daten auf dem Tag") {
                if let fields = TagInfo.fields(fromJSON: record.payload) {
                    ForEach(fields) { field in
                        LabeledContent {
                            HStack(spacing: 6) {
                                if field.key == "color_hex" {
                                    ColorDot(hex: field.value, size: 16, showsPlaceholder: false)
                                }
                                Text(value(of: field))
                            }
                        } label: {
                            Text(field.label)
                            if field.label != field.key {
                                Text(field.key)
                            }
                        }
                    }
                }
                if let error = tag.parseError {
                    Text(error).foregroundStyle(.red)
                }
                if let json = TagInfo.prettyJSON(record.payload) {
                    DisclosureGroup("JSON") {
                        Text(json)
                            .font(.caption.monospaced())
                            .textSelection(.enabled)
                    }
                }
            }
        }
    }

    @ViewBuilder
    private func chipSection(for tag: ScannedTag) -> some View {
        Section("Chip") {
            LabeledContent("UID") {
                Text(tag.details?.uid ?? tag.tagId).monospaced().textSelection(.enabled)
            }
            if let details = tag.details {
                InfoRow(label: "Typ", value: details.technology)
                if let status = details.ndefStatus { InfoRow(label: "NDEF", value: status) }
                if let capacity = details.capacity { InfoRow(label: "Speicher", value: "\(capacity) Bytes") }
            }
        }
    }

    @ViewBuilder
    private func recordsSection(for tag: ScannedTag) -> some View {
        if let records = tag.details?.records, !records.isEmpty {
            Section("NDEF-Records (\(records.count))") {
                ForEach(Array(records.enumerated()), id: \.offset) { index, record in
                    VStack(alignment: .leading, spacing: 4) {
                        Text("\(index + 1). \(record.formatName): \(record.type.isEmpty ? "ohne Typ" : record.type)")
                            .font(.subheadline.weight(.semibold))
                        Text("\(record.payload.count) Bytes").font(.caption).foregroundStyle(.secondary)
                        if !record.isJSON, !record.payload.isEmpty {
                            Text(record.payloadText)
                                .font(.caption.monospaced())
                                .textSelection(.enabled)
                        }
                    }
                }
            }
        }
    }

    // MARK: Hilfen

    private func value(of field: TagField) -> String {
        switch field.key {
        case "min_temp", "max_temp", "bed_min_temp", "bed_max_temp":
            return Double(field.value) != nil ? "\(field.value) °C" : field.value
        case "weight":
            return Double(field.value) != nil ? "\(field.value) g" : field.value
        case "diameter":
            return Double(field.value) != nil ? "\(field.value) mm" : field.value
        default:
            return field.value
        }
    }

    private func grams(_ value: Double) -> String {
        "\(value.formatted(.number.precision(.fractionLength(0)))) g"
    }

    private func scan() async {
        isScanning = true
        defer { isScanning = false }
        do {
            tag = try await NFCService.shared.scan(prompt: "Halten Sie das iPhone an einen Tag.")
            errorMessage = nil
        } catch NFCError.cancelled {
            // Nutzer hat das Scanfenster geschlossen
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
