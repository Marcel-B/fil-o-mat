import SwiftUI

/// Verbindung zum Fil-o-mat-Backend, über das alle Geräte dieselben Container und Filamente sehen.
struct BackendSettingsView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var serverURL = BackendSettings.serverURL
    @State private var apiKey = BackendSettings.apiKey
    @State private var resultMessage: String?
    @State private var confirmingUpdate = false
    @State private var isChecking = false
    @State private var updateError: String?
    @State private var isBackingUp = false
    @State private var backupError: String?

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
                if let message = store.compatibility?.message {
                    Section("Version") { Text(message).foregroundStyle(.orange) }
                }
                if let backup = store.backupStatus {
                    backupSection(backup)
                }
                if let update = store.updateStatus {
                    updateSection(update)
                }
            }
            .task {
                await store.refreshUpdateStatus()
                await store.refreshBackupStatus()
            }
            // Während der Aktualisierung den Stand verfolgen, bis das Backend wieder läuft
            .task(id: store.updateStatus?.isUpdating) {
                while store.updateStatus?.isUpdating == true, !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(3))
                    await store.refreshUpdateStatus()
                }
            }
            .confirmationDialog("Backend aktualisieren?", isPresented: $confirmingUpdate, titleVisibility: .visible) {
                Button("Jetzt aktualisieren") { Task { await installUpdate() } }
            } message: {
                Text("Der Server holt die neue Version, baut sie und startet das Backend neu. Das dauert ein paar Minuten; so lange ist kein Abgleich möglich.")
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

    @ViewBuilder
    private func updateSection(_ update: BackendUpdateStatus) -> some View {
        Section {
            if let version = update.currentVersion {
                LabeledContent("Läuft", value: Self.describe(version, date: update.currentDate))
            }
            if update.isUpdating {
                HStack {
                    Text("Wird aktualisiert …")
                    Spacer()
                    ProgressView()
                }
            } else if update.updateAvailable {
                ForEach(update.commits) { commit in
                    VStack(alignment: .leading) {
                        Text(commit.subject)
                        Text(Self.describe(commit.sha, date: commit.date))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                Button("Aktualisieren …") { confirmingUpdate = true }
            } else {
                Text("Das Backend ist auf dem neuesten Stand.")
                    .foregroundStyle(.secondary)
            }
            if update.state == "failed", let message = update.message {
                Text(message).foregroundStyle(.red)
            } else if let message = update.message, !update.isUpdating {
                Text(message).foregroundStyle(.secondary)
            }
            if let updateError {
                Text(updateError).foregroundStyle(.red)
            }
            if !update.isUpdating {
                Button {
                    Task { await checkForUpdate() }
                } label: {
                    HStack {
                        Text("Nach neuer Version suchen")
                        if isChecking {
                            Spacer()
                            ProgressView()
                        }
                    }
                }
                .disabled(isChecking)
            }
        } header: {
            if update.updateAvailable && !update.isUpdating {
                Text("Neue Version (\(update.commits.count) Commits)")
            } else {
                Text("Version")
            }
        } footer: {
            if let checkedAt = update.checkedAt {
                Text("Zuletzt geprüft \(checkedAt.formatted(date: .abbreviated, time: .shortened)), Branch \(update.branch ?? "?").")
            }
        }
    }

    @ViewBuilder
    private func backupSection(_ backup: BackupStatus) -> some View {
        Section {
            if !backup.configured {
                Text("Auf dem Server ist keine Nextcloud eingetragen (NEXTCLOUD_WEBDAV_URL, NEXTCLOUD_USERNAME, NEXTCLOUD_APP_PASSWORD in backend/.env).")
                    .foregroundStyle(.secondary)
            } else {
                if let last = backup.last {
                    LabeledContent("Letzte", value: last.finishedAt.formatted(date: .abbreviated, time: .shortened))
                    if last.success {
                        Label("Erfolgreich: \(last.archives.joined(separator: ", "))", systemImage: "checkmark.circle")
                            .foregroundStyle(.green)
                    } else {
                        Label(last.error ?? "Fehlgeschlagen", systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.red)
                    }
                } else {
                    Text("Noch keine Sicherung.").foregroundStyle(.secondary)
                }
                if let nextRun = backup.nextRun {
                    LabeledContent("Nächste", value: nextRun.formatted(date: .abbreviated, time: .shortened))
                }
                if let backupError {
                    Text(backupError).foregroundStyle(.red)
                }
                Button {
                    Task { await startBackup() }
                } label: {
                    HStack {
                        Text("Jetzt sichern")
                        if isBackingUp || backup.running {
                            Spacer()
                            ProgressView()
                        }
                    }
                }
                .disabled(isBackingUp || backup.running)
            }
        } header: {
            Text("Sicherung in die Nextcloud")
        }
    }

    private func startBackup() async {
        backupError = nil
        isBackingUp = true
        defer { isBackingUp = false }
        do {
            try await store.startBackup()
        } catch {
            backupError = error.localizedDescription
        }
    }

    private static func describe(_ sha: String, date: Date?) -> String {
        let short = String(sha.prefix(7))
        guard let date else { return short }
        return String(localized: "\(short) vom \(date.formatted(date: .abbreviated, time: .shortened))")
    }

    private func checkForUpdate() async {
        updateError = nil
        isChecking = true
        defer { isChecking = false }
        let previous = store.updateStatus?.checkedAt
        do {
            try await store.checkForUpdate()
            // Der Updater auf dem Server braucht ein paar Sekunden für git fetch
            for _ in 0..<10 {
                try await Task.sleep(for: .seconds(1))
                await store.refreshUpdateStatus()
                if store.updateStatus?.checkedAt != previous { break }
            }
        } catch {
            updateError = error.localizedDescription
        }
    }

    private func installUpdate() async {
        updateError = nil
        do {
            try await store.installUpdate()
        } catch {
            updateError = error.localizedDescription
        }
    }

    private func connect() async {
        guard let client = BackendSettings.client(serverURL: serverURL, apiKey: apiKey) else { return }
        BackendSettings.serverURL = serverURL
        BackendSettings.apiKey = apiKey
        resultMessage = nil
        guard let result = await store.connect(client) else { return }
        var message = String(localized: "Hochgeladen: \(result.containers) Container, \(result.items) Filamente. Jetzt \(store.containers.count) Container und \(store.items.count) Filamente.")
        for skipped in result.skipped { message += "\n• \(skipped)" }
        resultMessage = message
    }
}
