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
                Label(live.warnings.count == 1 ? live.warnings[0].text : String(localized: "\(live.warnings.count) Warnungen"),
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
    @State private var isFixing = false

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
                            if hint.kind == "TagMismatch", let tool = hint.tool, let slot = live.tools.first(where: { $0.tool == tool }) {
                                detectedFix(for: slot)
                            }
                        }
                    }
                }
                if !live.tools.isEmpty {
                    Section(live.tools.count > 1 ? (printer.type == .moonraker || printer.type == .snapmakerU1 ? "Köpfe" : "Slots") : "Spule") {
                        ForEach(live.tools) { tool in
                            ToolLiveRow(tool: tool, isActive: live.status.isActive && live.status.activeTool == tool.tool && live.tools.count > 1)
                            spoolChoice(for: tool)
                        }
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
                LabeledContent("Schicht", value: String(localized: "\(layer) von \(total)"))
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

    /// Der Drucker erkennt das Plättchen einer Sorte, aber welche ihrer losen Spulen steckt dort?
    @ViewBuilder
    private func spoolChoice(for tool: ToolLive) -> some View {
        if tool.spoolId == nil, let itemId = tool.itemId {
            let loose = store.spools(of: itemId).filter { $0.printerId == nil }
            if !loose.isEmpty {
                Menu {
                    ForEach(loose) { spool in
                        Button(spool.remainingWeight.map { "#\(spool.number) · \($0.gramsText)" } ?? "#\(spool.number)") {
                            Task { await place(spool, tool: tool.tool) }
                        }
                    }
                } label: {
                    Label("Welche Spule steckt hier?", systemImage: "questionmark.circle")
                }
            }
        }
    }

    /// Der Drucker erkennt das Plättchen einer anderen Sorte als die Spule, die laut App dort steckt: ein Tipp legt eine
    /// Spule der erkannten Sorte hinein, die bisherige wandert ins Lager. Bei mehreren Spulen der Sorte wird gewählt.
    @ViewBuilder
    private func detectedFix(for tool: ToolLive) -> some View {
        if let tag = tool.reported?.tagUid, let item = store.items.first(where: { $0.allTags.contains { $0.caseInsensitiveCompare(tag) == .orderedSame } }),
           item.id != tool.itemId {
            let candidates = store.spools(of: item.id)
                .filter { $0.printerId != printer.id || ($0.tool ?? 0) != tool.tool }
                .sorted { ($0.printerId == nil ? 0 : 1, $0.number) < ($1.printerId == nil ? 0 : 1, $1.number) }
            Group {
                if candidates.count == 1, let spool = candidates.first {
                    Button {
                        Task { await place(spool, tool: tool.tool) }
                    } label: {
                        Label("\(item.name) #\(spool.number) übernehmen", systemImage: "arrow.triangle.2.circlepath")
                    }
                } else if candidates.count > 1 {
                    Menu {
                        ForEach(candidates) { spool in
                            Button(candidateTitle(spool)) {
                                Task { await place(spool, tool: tool.tool) }
                            }
                        }
                    } label: {
                        Label("\(item.name) übernehmen", systemImage: "arrow.triangle.2.circlepath")
                    }
                } else if (item.spoolsInStock ?? 0) > 0 {
                    Button {
                        Task { await openSpool(of: item, tool: tool.tool) }
                    } label: {
                        Label("Neue Spule \(item.name) anbrechen", systemImage: "plus.circle")
                    }
                }
            }
            .disabled(isFixing)
        }
    }

    /// "#2 · 384 g", bei Spulen in einem anderen Drucker mit dessen Namen.
    private func candidateTitle(_ spool: Spool) -> String {
        var parts = ["#\(spool.number)"]
        if let grams = spool.remainingWeight { parts.append(grams.gramsText) }
        if let printerId = spool.printerId, let other = store.printers.first(where: { $0.id == printerId }) { parts.append(other.name) }
        return parts.joined(separator: " · ")
    }

    private func openSpool(of item: Item, tool: Int) async {
        isFixing = true
        defer { isFixing = false }
        _ = try? await store.openSpool(itemId: item.id, place: .printer(printer.id, tool: tool))
        await store.refreshPrinterLive()
    }

    private func place(_ spool: Spool, tool: Int) async {
        isFixing = true
        defer { isFixing = false }
        try? await store.placeSpool(spool, at: .printer(printer.id, tool: tool))
        await store.refreshPrinterLive()
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
