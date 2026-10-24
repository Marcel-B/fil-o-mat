import SwiftUI

/// Eine Zeile Zustand: "Druckt benchy · 45 % · noch 32 min", dazu die Zahl der Warnungen.
struct PrinterStatusLine: View {
    var live: PrinterLive

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label(live.status.summary, systemImage: live.status.symbol)
                .foregroundStyle(color(for: live.status))
            if live.status.isActive, let progress = live.status.progress {
                ProgressView(value: min(max(progress, 0), 100), total: 100)
                    .tint(live.status.state == "Paused" ? .orange : .accentColor)
            }
            if !live.warnings.isEmpty {
                Label(live.warnings.count == 1 ? live.warnings[0].text : "\(live.warnings.count) Warnungen",
                      systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                    .lineLimit(2)
            }
        }
        .font(.caption)
    }
}

private func color(for status: PrinterStatus) -> Color {
    switch status.state {
    case "Printing": .accentColor
    case "Paused": .orange
    case "Error": .red
    case "Offline", "Idle": .secondary
    default: .primary
    }
}

/// Zustand eines Druckers im Detail: Druck, Vorschau, Hinweise, Spulen pro Kopf, Temperaturen, Feuchte.
struct PrinterDetailView: View {
    @Environment(ContainerStore.self) private var store
    var printer: Printer
    var onChanged: () async -> Void

    @State private var thumbnail: UIImage?
    @State private var showingEdit = false

    private var live: PrinterLive? { store.printerLive[printer.id] }

    var body: some View {
        List {
            if let live {
                statusSection(live)
                if !live.hints.isEmpty {
                    Section("Hinweise") {
                        ForEach(live.hints) { hint in
                            Label(hint.text, systemImage: hint.symbol)
                                .foregroundStyle(hint.warning ? Color.orange : Color.secondary)
                        }
                    }
                }
                if !live.tools.isEmpty {
                    Section(live.tools.count > 1 ? (printer.type == .moonraker || printer.type == .snapmakerU1 ? "Köpfe" : "Slots") : "Spule") {
                        ForEach(live.tools) { ToolLiveRow(tool: $0, isActive: live.status.isActive && live.status.activeTool == $0.tool && live.tools.count > 1) }
                    }
                }
                if let heaters = live.status.heaters, !heaters.isEmpty {
                    Section("Temperaturen") {
                        ForEach(heaters, id: \.name) { LabeledContent($0.name, value: $0.text) }
                    }
                }
                if let dryers = live.status.dryers, !dryers.isEmpty {
                    Section("Trockenbox") {
                        ForEach(dryers, id: \.name) { LabeledContent($0.name, value: $0.text) }
                    }
                }
            } else {
                ContentUnavailableView("Kein Zustand", systemImage: "printer",
                                       description: Text("Das Backend hat den Drucker noch nicht abgefragt oder kennt keinen Zustand (ältere Version?)."))
            }
        }
        .navigationTitle(printer.name)
        .toolbar {
            Button("Bearbeiten") { showingEdit = true }
        }
        .sheet(isPresented: $showingEdit) {
            PrinterFormView(printer: printer) { await onChanged() }
        }
        .refreshable { await store.refreshPrinterLive() }
        // Solange die Ansicht offen ist, alle 15 Sekunden nachfragen
        .task {
            while !Task.isCancelled {
                await store.refreshPrinterLive()
                try? await Task.sleep(for: .seconds(15))
            }
        }
        .task(id: live?.status.jobKey) { await loadThumbnail() }
    }

    @ViewBuilder
    private func statusSection(_ live: PrinterLive) -> some View {
        let status = live.status
        Section {
            Label(status.title, systemImage: status.symbol)
                .font(.headline)
                .foregroundStyle(color(for: status))
            if status.hasJob, let job = status.jobName, !job.isEmpty {
                LabeledContent("Druck", value: job)
            }
            if status.isActive, let progress = status.progress {
                VStack(alignment: .leading, spacing: 4) {
                    ProgressView(value: min(max(progress, 0), 100), total: 100)
                    HStack {
                        Text("\(Int(progress)) %")
                        Spacer()
                        if let remaining = status.remainingText { Text(remaining) }
                    }
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }
            }
            if let layer = status.layer, let total = status.totalLayers, total > 0, status.hasJob {
                LabeledContent("Schicht", value: "\(layer) von \(total)")
            }
            if let elapsed = status.elapsedSeconds, elapsed > 0, status.hasJob {
                LabeledContent("Druckzeit", value: PrinterStatus.duration(elapsed))
            }
            if let message = status.message, !message.isEmpty {
                Text(message).font(.caption).foregroundStyle(status.state == "Error" || status.state == "Offline" ? Color.red : Color.secondary)
            }
            if let thumbnail, status.hasJob {
                HStack {
                    Spacer()
                    Image(uiImage: thumbnail)
                        .resizable()
                        .scaledToFit()
                        .frame(maxHeight: 180)
                    Spacer()
                }
            }
        } footer: {
            if let date = live.updatedDate {
                Text("Abgefragt \(date.formatted(.relative(presentation: .named)))")
            }
        }
    }

    private func loadThumbnail() async {
        guard let backend = store.backend, live?.status.hasThumbnail == true else {
            thumbnail = nil
            return
        }
        if let data = try? await backend.printerThumbnail(id: printer.id), let image = UIImage(data: data) {
            thumbnail = image
        }
    }
}

/// Ein Kopf bzw. Slot: Spule laut App, Restgewicht und was der Druck noch braucht.
private struct ToolLiveRow: View {
    var tool: ToolLive
    var isActive: Bool

    var body: some View {
        HStack(spacing: 12) {
            ColorDot(hex: tool.colorHex, size: 28)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 4) {
                    if !tool.place.isEmpty { Text(tool.place).foregroundStyle(.secondary) }
                    if isActive { Image(systemName: "arrowtriangle.right.fill").font(.caption2).foregroundStyle(.tint) }
                }
                .font(.caption)
                Text(tool.title)
                    .foregroundStyle(tool.itemName == nil ? .secondary : .primary)
                if let percent = tool.reported?.remainPercent {
                    Text("Drucker meldet \(Int(percent)) % Rest").font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 2) {
                if let remaining = tool.remainingGrams {
                    Text(remaining.gramsText)
                }
                if let needed = tool.neededGrams {
                    Text("braucht \(needed.gramsText)")
                        .font(.caption)
                        .foregroundStyle(tool.enough == false ? Color.red : Color.secondary)
                }
            }
            .monospacedDigit()
        }
    }
}
