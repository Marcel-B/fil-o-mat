import SwiftUI

@main
struct FilomatApp: App {
    @State private var store: ContainerStore = {
        let store = ContainerStore()
        store.backend = BackendSettings.client()
        return store
    }()
    @Environment(\.scenePhase) private var scenePhase

    static let refreshInterval: Duration = .seconds(120)

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(store)
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
        // Auf iPad und Mac als Seitenleiste, auf dem iPhone als Tabs
        if #available(iOS 18.0, *) {
            TabView {
                Tab("Container", systemImage: "archivebox") {
                    NavigationStack { ContainerListView() }
                }
                Tab(ScanView.title, systemImage: ScanView.systemImage) {
                    NavigationStack { ScanView() }
                }
                Tab("Filamente", systemImage: "magnifyingglass") {
                    NavigationStack { ItemsOverviewView() }
                }
                Tab("Drucker", systemImage: "printer") {
                    NavigationStack { PrintersView() }
                }
            }
            .tabViewStyle(.sidebarAdaptable)
        } else {
            legacyTabs
        }
    }

    private var legacyTabs: some View {
        TabView {
            NavigationStack { ContainerListView() }
                .tabItem { Label("Container", systemImage: "archivebox") }
            NavigationStack { ScanView() }
                .tabItem { Label(ScanView.title, systemImage: ScanView.systemImage) }
            NavigationStack { ItemsOverviewView() }
                .tabItem { Label("Filamente", systemImage: "magnifyingglass") }
            NavigationStack { PrintersView() }
                .tabItem { Label("Drucker", systemImage: "printer") }
        }
    }
}
