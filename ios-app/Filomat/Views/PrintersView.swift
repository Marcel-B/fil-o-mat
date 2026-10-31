import SwiftUI

/// Drucker im Backend: anlegen, bearbeiten, abgleichen, entfernen.
struct PrintersView: View {
    @Environment(ContainerStore.self) private var store
    @State private var printers: [Printer] = []
    @State private var errorMessage: String?
    @State private var showingAdd = false
    @State private var printerToDelete: Printer?
    @State private var printerToEdit: Printer?
    @State private var syncingId: String?
    @State private var showingNotifications = false

    var body: some View {
        List {
            if store.backend == nil {
                ContentUnavailableView(
                    "Kein Backend verbunden",
                    systemImage: "icloud.slash",
                    description: Text("Drucker werden im Backend verwaltet. Verbinde es über das Wolken-Symbol in der Container-Liste.")
                )
            }
            if let errorMessage {
                Text(errorMessage).foregroundStyle(.red)
            }
            ForEach(printers) { printer in
                NavigationLink {
                    PrinterDetailView(printer: printer) { await load() }
                } label: {
                    PrinterRow(printer: printer, live: store.printerLive[printer.id], isSyncing: syncingId == printer.id)
                }
                .swipeActions {
                    Button(role: .destructive) {
                        printerToDelete = printer
                    } label: {
                        Label("Entfernen", systemImage: "trash")
                    }
                    Button {
                        Task { await sync(printer) }
                    } label: {
                        Label("Abgleichen", systemImage: "arrow.triangle.2.circlepath")
                    }
                    .tint(.accentColor)
                    Button {
                        printerToEdit = printer
                    } label: {
                        Label("Bearbeiten", systemImage: "pencil")
                    }
                }
            }
        }
        .overlay {
            if store.backend != nil && printers.isEmpty && errorMessage == nil {
                ContentUnavailableView("Keine Drucker", systemImage: "printer",
                                       description: Text("Füge einen Drucker hinzu, damit sein Verbrauch automatisch abgezogen wird."))
            }
        }
        .navigationTitle("Drucker")
        .toolbar {
            ToolbarItemGroup(placement: .topBarLeading) {
                NavigationLink {
                    StatsView()
                } label: {
                    Label("Statistik", systemImage: "chart.bar")
                }
                .disabled(store.backend == nil)
                Button {
                    showingNotifications = true
                } label: {
                    Label("Meldungen", systemImage: "bell")
                }
                .disabled(store.backend == nil)
            }
            ToolbarItem(placement: .primaryAction) {
                Button {
                    showingAdd = true
                } label: {
                    Label("Drucker hinzufügen", systemImage: "plus")
                }
                .disabled(store.backend == nil)
            }
        }
        .refreshable { await syncAll() }
        // Solange die Liste offen ist, den Zustand alle 15 Sekunden holen
        .task {
            await load()
            while !Task.isCancelled {
                await store.refreshPrinterLive()
                try? await Task.sleep(for: .seconds(15))
            }
        }
        .sheet(isPresented: $showingNotifications) {
            NotificationSettingsView()
        }
        .sheet(isPresented: $showingAdd) {
            PrinterFormView(printer: nil) { await load() }
        }
        .sheet(item: $printerToEdit) { printer in
            PrinterFormView(printer: printer) { await load() }
        }
        .alert(
            "Drucker entfernen",
            isPresented: Binding(get: { printerToDelete != nil }, set: { if !$0 { printerToDelete = nil } }),
            presenting: printerToDelete
        ) { printer in
            Button("Entfernen", role: .destructive) { Task { await delete(printer) } }
            Button("Abbrechen", role: .cancel) {}
        } message: { printer in
            Text("'\(printer.name)' und sein Container werden entfernt. Eingelegte Spulen wandern nach „Lose“, ihr Verbrauch bleibt erhalten.")
        }
    }

    private func load() async {
        guard let backend = store.backend else {
            printers = []
            return
        }
        do {
            printers = try await backend.printers()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func sync(_ printer: Printer) async {
        guard let backend = store.backend else { return }
        syncingId = printer.id
        defer { syncingId = nil }
        do {
            _ = try await backend.syncPrinter(id: printer.id)
            await load()
            await store.refresh()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Gleicht alle aktiven Drucker ab (Ziehen zum Aktualisieren) und lädt danach Drucker und Spulen neu.
    private func syncAll() async {
        guard let backend = store.backend else { return }
        await load()
        for printer in printers where printer.enabled {
            syncingId = printer.id
            _ = try? await backend.syncPrinter(id: printer.id)
        }
        syncingId = nil
        await load()
        await store.refresh()
    }

    private func delete(_ printer: Printer) async {
        guard let backend = store.backend else { return }
        do {
            try await backend.deletePrinter(id: printer.id)
            await load()
            await store.refresh()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

private struct PrinterRow: View {
    var printer: Printer
    var live: PrinterLive?
    var isSyncing: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(printer.name).font(.headline)
                Spacer()
                if isSyncing { ProgressView() }
            }
            Text((printer.enabled ? "" : String(localized: "Pausiert · ")) + (printer.toolCount > 1
                 ? String(localized: "\(printer.type.title) · \(printer.toolCount) Köpfe · \(printer.baseUrl)")
                 : "\(printer.type.title) · \(printer.baseUrl)"))
                .font(.caption)
                .foregroundStyle(.secondary)
            if let live, printer.enabled, printer.lastSyncError == nil {
                PrinterStatusLine(live: live)
            }
            if let error = printer.lastSyncError {
                Label(error, systemImage: "exclamationmark.triangle")
                    .font(.caption)
                    .foregroundStyle(.red)
            } else if let date = printer.lastSyncDate {
                Text("Zuletzt abgeglichen \(date.formatted(.relative(presentation: .named)))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 2)
    }
}

/// Neuer Drucker (`printer` ist `nil`) oder Änderungen an einem vorhandenen, z. B. eine neue IP nach DHCP.
struct PrinterFormView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    var printer: Printer?
    var onSaved: () async -> Void

    @State private var name: String
    @State private var type: PrinterType
    @State private var baseUrl: String
    @State private var apiKey = ""
    @State private var removeApiKey = false
    @State private var enabled: Bool
    @State private var isSaving = false
    @State private var errorMessage: String?

    init(printer: Printer?, onSaved: @escaping () async -> Void) {
        self.printer = printer
        self.onSaved = onSaved
        _name = State(initialValue: printer?.name ?? "")
        _type = State(initialValue: printer?.type ?? .snapmakerU1)
        _baseUrl = State(initialValue: printer?.baseUrl ?? "")
        _enabled = State(initialValue: printer?.enabled ?? true)
    }

    private var trimmedUrl: String { baseUrl.trimmingCharacters(in: .whitespaces) }

    private var isValid: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && (trimmedUrl.hasPrefix("http://") || trimmedUrl.hasPrefix("https://"))
            && (printer != nil || !type.requiresKey || !apiKey.trimmingCharacters(in: .whitespaces).isEmpty)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name, z. B. Snapmaker U1", text: $name)
                    if printer == nil {
                        Picker("Typ", selection: $type) {
                            ForEach(PrinterType.allCases) { Text($0.title).tag($0) }
                        }
                    } else {
                        LabeledContent("Typ", value: type.title)
                    }
                    TextField(type.addressHint, text: $baseUrl)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    if printer?.hasApiKey == true {
                        SecureField(type.requiresKey ? "Neuer Zugangscode (leer = unverändert)" : "Neuer API-Schlüssel (leer = unverändert)", text: $apiKey)
                            .disabled(removeApiKey)
                        if !type.requiresKey {
                            Toggle("API-Schlüssel entfernen", isOn: $removeApiKey)
                        }
                    } else {
                        SecureField(type.keyLabel, text: $apiKey)
                    }
                } footer: {
                    Text(type.setupHint)
                }
                if printer != nil {
                    Section {
                        Toggle("Automatisch abgleichen", isOn: $enabled)
                    } footer: {
                        Text("Ausgeschaltet fragt das Backend den Drucker nicht mehr ab, z. B. solange er nicht im Netz ist.")
                    }
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle(printer == nil ? "Drucker hinzufügen" : "Drucker bearbeiten")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button(printer == nil ? "Hinzufügen" : "Sichern") { Task { await save() } }
                        .disabled(!isValid || isSaving)
                }
            }
        }
    }

    private func save() async {
        guard let backend = store.backend else { return }
        isSaving = true
        defer { isSaving = false }
        let key = apiKey.trimmingCharacters(in: .whitespaces)
        do {
            if let printer {
                try await backend.updatePrinter(id: printer.id, PrinterUpdateRequest(
                    name: name.trimmingCharacters(in: .whitespaces),
                    baseUrl: trimmedUrl,
                    apiKey: removeApiKey ? "" : (key.isEmpty ? nil : key),
                    enabled: enabled
                ))
            } else {
                try await backend.createPrinter(PrinterRequest(
                    name: name.trimmingCharacters(in: .whitespaces),
                    type: type,
                    baseUrl: trimmedUrl,
                    apiKey: key.isEmpty ? nil : key
                ))
            }
            await store.refresh()
            await onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
