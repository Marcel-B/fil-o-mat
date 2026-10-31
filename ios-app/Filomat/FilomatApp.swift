import SwiftUI

@main
struct FilomatApp: App {
    @State private var store: ContainerStore = {
        let store = ContainerStore()
        store.backend = BackendSettings.client()
        return store
    }()
    @Environment(\.scenePhase) private var scenePhase
    @AppStorage(Appearance.storageKey) private var appearance = Appearance.system

    static let refreshInterval: Duration = .seconds(120)

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(store)
                .preferredColorScheme(appearance.colorScheme)
                // Beim Öffnen und danach alle 2 Minuten den Stand holen, solange die App im Vordergrund ist
                // (das Backend fragt die Drucker im selben Takt ab)
                .task(id: scenePhase) {
                    while scenePhase == .active, !Task.isCancelled {
                        await store.refresh()
                        await store.refreshUpdateStatus()
                        try? await Task.sleep(for: FilomatApp.refreshInterval)
                    }
                }
        }
    }
}

struct ContentView: View {
    var body: some View {
        TabView {
            NavigationStack { ContainerListView() }
                .tabItem { Label("Container", systemImage: "archivebox") }
            NavigationStack { ScanView() }
                .tabItem { Label("Scannen", systemImage: "wave.3.right") }
            NavigationStack { ItemsOverviewView() }
                .tabItem { Label("Filamente", systemImage: "magnifyingglass") }
            NavigationStack { PrintersView() }
                .tabItem { Label("Drucker", systemImage: "printer") }
        }
    }
}
