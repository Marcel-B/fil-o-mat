import SwiftUI

/// Hell, dunkel oder wie das System; gespeichert in den UserDefaults und in `FilomatApp` angewendet.
enum Appearance: String, CaseIterable, Identifiable {
    case system, light, dark

    static let storageKey = "appearance"

    var id: String { rawValue }

    var title: LocalizedStringKey {
        switch self {
        case .system: "Wie das System"
        case .light: "Hell"
        case .dark: "Dunkel"
        }
    }

    var colorScheme: ColorScheme? {
        switch self {
        case .system: nil
        case .light: .light
        case .dark: .dark
        }
    }
}

/// Einstellungen der App selbst: Farbschema und Sprache.
struct AppSettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @AppStorage(Appearance.storageKey) private var appearance = Appearance.system

    var body: some View {
        NavigationStack {
            Form {
                Section("Darstellung") {
                    Picker("Farbschema", selection: $appearance) {
                        ForEach(Appearance.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
                Section {
                    LabeledContent("Sprache", value: Locale.current.localizedString(forLanguageCode: Bundle.main.preferredLocalizations.first ?? "de") ?? "")
                    Button("In den Systemeinstellungen ändern") {
                        if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
                    }
                } header: {
                    Text("Sprache")
                } footer: {
                    Text("Die App gibt es auf Deutsch und Englisch. Sie folgt der Sprache des Geräts; eine eigene Sprache nur für Fil-o-mat stellst du in den Systemeinstellungen ein.")
                }
            }
            .navigationTitle("Einstellungen")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { dismiss() }
                }
            }
        }
    }
}
