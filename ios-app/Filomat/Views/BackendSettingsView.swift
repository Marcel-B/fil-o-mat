import SwiftUI

/// Verbindung zum Fil-o-mat-Backend, über das alle Geräte dieselben Container und Filamente sehen.
struct BackendSettingsView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var serverURL = BackendSettings.serverURL
    @State private var apiKey = BackendSettings.apiKey
    @State private var resultMessage: String?

    private var isValid: Bool { BackendSettings.client(serverURL: serverURL, apiKey: apiKey) != nil }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("http://nas.local:8080", text: $serverURL)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    SecureField("API-Schlüssel (optional)", text: $apiKey)
                } header: {
                    Text("Server")
                } footer: {
                    Text("Beim Verbinden werden die Daten dieses iPhones hochgeladen (nichts wird gelöscht) und danach der gemeinsame Stand übernommen, z. B. Container aus der Android-App.")
                }

                Section {
                    Button {
                        Task { await connect() }
                    } label: {
                        HStack {
                            Text(store.backend == nil ? "Verbinden und abgleichen" : "Erneut abgleichen")
                            if store.isSyncing {
                                Spacer()
                                ProgressView()
                            }
                        }
                    }
                    .disabled(!isValid || store.isSyncing)

                    if store.backend != nil {
                        Button("Verbindung trennen", role: .destructive) {
                            BackendSettings.serverURL = ""
                            BackendSettings.apiKey = ""
                            serverURL = ""
                            apiKey = ""
                            resultMessage = nil
                            store.disconnect()
                        }
                    }
                }

                if let resultMessage {
                    Section("Ergebnis") { Text(resultMessage) }
                }
                if let error = store.syncError {
                    Section("Fehler") { Text(error).foregroundStyle(.red) }
                }
                if let lastSyncedAt = store.lastSyncedAt {
                    Section {
                        LabeledContent("Letzter Abgleich", value: lastSyncedAt.formatted(date: .omitted, time: .shortened))
                    }
                }
            }
            .navigationTitle("Backend")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { dismiss() }
                }
            }
        }
    }

    private func connect() async {
        guard let client = BackendSettings.client(serverURL: serverURL, apiKey: apiKey) else { return }
        BackendSettings.serverURL = serverURL
        BackendSettings.apiKey = apiKey
        resultMessage = nil
        guard let result = await store.connect(client) else { return }
        var message = "Hochgeladen: \(result.containers) Container, \(result.items) Filamente. Jetzt \(store.containers.count) Container und \(store.items.count) Filamente."
        for skipped in result.skipped { message += "\n• \(skipped)" }
        resultMessage = message
    }
}
