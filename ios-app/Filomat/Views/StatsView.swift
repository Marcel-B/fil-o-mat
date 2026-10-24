import Charts
import SwiftUI

/// Verbrauch, Drucke und Druckzeit pro Monat, Drucker und Sorte.
struct StatsView: View {
    @Environment(ContainerStore.self) private var store
    @State private var months = 12
    @State private var stats: StatsResponse?
    @State private var errorMessage: String?

    var body: some View {
        List {
            Section {
                Picker("Zeitraum", selection: $months) {
                    Text("3 Monate").tag(3)
                    Text("12 Monate").tag(12)
                    Text("2 Jahre").tag(24)
                }
                .pickerStyle(.segmented)
            }
            if let errorMessage {
                Text(errorMessage).foregroundStyle(.red)
            }
            if let stats {
                Section("Gesamt") {
                    LabeledContent("Verbrauch", value: stats.totals.grams.gramsText)
                    LabeledContent("Drucke", value: stats.totals.cancelled > 0
                                   ? "\(stats.totals.jobs) (\(stats.totals.cancelled) abgebrochen)"
                                   : "\(stats.totals.jobs)")
                    if stats.totals.printSeconds > 0 {
                        LabeledContent("Druckzeit", value: PrinterStatus.duration(stats.totals.printSeconds))
                    }
                    if stats.totals.cancelledGrams > 0 {
                        LabeledContent("Davon abgebrochen", value: stats.totals.cancelledGrams.gramsText)
                    }
                }
                if stats.totals.grams > 0 {
                    Section("Verbrauch pro Monat") {
                        Chart(stats.months) { month in
                            BarMark(x: .value("Monat", months > 12 ? month.yearLabel : month.label), y: .value("Gramm", month.grams))
                        }
                        .chartYAxis {
                            AxisMarks { value in
                                AxisGridLine()
                                AxisValueLabel { if let grams = value.as(Double.self) { Text(grams.gramsText) } }
                            }
                        }
                        .frame(height: 180)
                        .padding(.vertical, 8)
                    }
                }
                if !stats.printers.isEmpty {
                    Section("Drucker") {
                        ForEach(stats.printers) { printer in
                            VStack(alignment: .leading, spacing: 2) {
                                HStack {
                                    Text(printer.name)
                                    Spacer()
                                    Text(printer.totals.grams.gramsText).monospacedDigit()
                                }
                                Text(detail(printer.totals)).font(.caption).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
                if !stats.items.isEmpty {
                    Section("Sorten") {
                        ForEach(stats.items.prefix(15)) { item in
                            HStack(spacing: 12) {
                                ColorDot(hex: item.colorHex, size: 24, showsPlaceholder: false)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(item.name)
                                    Text(item.jobs == 1 ? "1 Druck" : "\(item.jobs) Drucke").font(.caption).foregroundStyle(.secondary)
                                }
                                Spacer()
                                Text(item.grams.gramsText).monospacedDigit()
                            }
                        }
                    }
                }
                if stats.types.count > 1 {
                    Section("Material") {
                        ForEach(stats.types) { LabeledContent($0.type, value: $0.grams.gramsText) }
                    }
                }
            } else if errorMessage == nil {
                HStack { Spacer(); ProgressView(); Spacer() }
            }
        }
        .navigationTitle("Statistik")
        .refreshable { await load() }
        .task(id: months) { await load() }
    }

    private func detail(_ totals: StatsTotals) -> String {
        var parts = [totals.jobs == 1 ? "1 Druck" : "\(totals.jobs) Drucke"]
        if totals.cancelled > 0 { parts.append("\(totals.cancelled) abgebrochen") }
        if totals.printSeconds > 0 { parts.append(PrinterStatus.duration(totals.printSeconds)) }
        return parts.joined(separator: " · ")
    }

    private func load() async {
        guard let backend = store.backend else {
            errorMessage = "Kein Backend verbunden."
            return
        }
        do {
            stats = try await backend.stats(months: months)
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

/// ntfy-Adresse für Meldungen bei Druckende, Fehlern und Spulen, die nicht reichen.
struct NotificationSettingsView: View {
    @Environment(ContainerStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var settings: NotificationSettings?
    @State private var url = ""
    @State private var token = ""
    @State private var isWorking = false
    @State private var message: String?
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("https://ntfy.sh/filomat-…", text: $url)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    SecureField(settings?.hasToken == true ? "Token (leer = unverändert)" : "Token (optional)", text: $token)
                } header: {
                    Text("ntfy-Thema")
                } footer: {
                    Text("Das Backend meldet fertige und abgebrochene Drucke mit Restgewicht, Fehler und Spulen, die für den laufenden Druck nicht reichen. Ein schwer erratbares Thema auf ntfy.sh reicht; in der ntfy-App abonnieren. Ein Token ist nur für einen geschützten eigenen Server nötig.")
                }
                if let settings, settings.configured, !settings.fromApp {
                    Section {
                        Text("Zurzeit gilt die Adresse aus der Server-Konfiguration. Eine Adresse hier geht vor.")
                            .font(.caption)
                    }
                }
                Section {
                    Button("Testmeldung schicken") { Task { await test() } }
                        .disabled(isWorking || settings?.configured != true)
                }
                if let message {
                    Section { Text(message).foregroundStyle(.secondary) }
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Meldungen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Schließen") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Sichern") { Task { await save() } }
                        .disabled(isWorking)
                }
            }
            .task { await load() }
        }
    }

    private func load() async {
        guard let backend = store.backend else { return }
        do {
            let loaded = try await backend.notificationSettings()
            settings = loaded
            url = loaded.fromApp ? loaded.url ?? "" : ""
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func save() async {
        guard let backend = store.backend else { return }
        isWorking = true
        defer { isWorking = false }
        let trimmedUrl = url.trimmingCharacters(in: .whitespaces)
        let trimmedToken = token.trimmingCharacters(in: .whitespaces)
        do {
            settings = try await backend.saveNotificationSettings(NotificationSettingsRequest(
                url: trimmedUrl, token: trimmedToken.isEmpty ? nil : trimmedToken))
            token = ""
            errorMessage = nil
            message = trimmedUrl.isEmpty ? "Adresse entfernt." : "Gesichert."
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func test() async {
        guard let backend = store.backend else { return }
        isWorking = true
        defer { isWorking = false }
        do {
            try await backend.sendTestNotification()
            errorMessage = nil
            message = "Testmeldung verschickt."
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
