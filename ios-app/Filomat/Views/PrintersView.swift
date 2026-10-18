import SwiftUI

/// Drucker im Backend: anlegen, abgleichen, entfernen.
struct PrintersView: View {
    @Environment(ContainerStore.self) private var store
    @State private var printers: [Printer] = []
    @State private var errorMessage: String?
    @State private var showingAdd = false
    @State private var printerToDelete: Printer?
    @State private var syncingId: String?

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
                PrinterRow(printer: printer, isSyncing: syncingId == printer.id)
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
            Button {
                showingAdd = true
            } label: {
                Label("Drucker hinzufügen", systemImage: "plus")
            }
            .disabled(store.backend == nil)
        }
        .refreshable { await load() }
        .task { await load() }
        .sheet(isPresented: $showingAdd) {
            AddPrinterView { await load() }
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
    var isSyncing: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(printer.name).font(.headline)
                Spacer()
                if isSyncing { ProgressView() }
            }
            Text(printer.toolCount > 1
                 ? "\(printer.type.title) · \(printer.toolCount) Köpfe · \(printer.baseUrl)"
                 : "\(printer.type.title) · \(printer.baseUrl)")
                .font(.caption)
                .foregroundStyle(.secondary)
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

private struct AddPrinterView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    var onAdded: () async -> Void

    @State private var name = ""
    @State private var type: PrinterType = .snapmakerU1
    @State private var baseUrl = ""
    @State private var apiKey = ""
    @State private var isSaving = false
    @State private var errorMessage: String?

    private var isValid: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && (baseUrl.hasPrefix("http://") || baseUrl.hasPrefix("https://"))
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name, z. B. Snapmaker U1", text: $name)
                    Picker("Typ", selection: $type) {
                        ForEach(PrinterType.allCases) { Text($0.title).tag($0) }
                    }
                    TextField(type.addressHint, text: $baseUrl)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    SecureField("API-Schlüssel (optional)", text: $apiKey)
                } footer: {
                    Text(type == .snapmakerU1
                         ? "Der U1 erkennt die Spulen in jedem Kopf über ihre Tags. Der Verbrauch wird pro Kopf abgezogen."
                         : "Der Verbrauch wird von der Spule abgezogen, die im Container dieses Druckers liegt.")
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Drucker hinzufügen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Abbrechen") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Hinzufügen") { Task { await save() } }
                        .disabled(!isValid || isSaving)
                }
            }
        }
    }

    private func save() async {
        guard let backend = store.backend else { return }
        isSaving = true
        defer { isSaving = false }
        do {
            try await backend.createPrinter(PrinterRequest(
                name: name.trimmingCharacters(in: .whitespaces),
                type: type,
                baseUrl: baseUrl.trimmingCharacters(in: .whitespaces),
                apiKey: apiKey.isEmpty ? nil : apiKey
            ))
            await store.refresh()
            await onAdded()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
