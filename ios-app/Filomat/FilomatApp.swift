import SwiftUI

@main
struct FilomatApp: App {
    @State private var store: ContainerStore = {
        let store = ContainerStore()
        store.backend = BackendSettings.client()
        return store
    }()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(store)
                .task {
                    await store.refresh()
                    await store.refreshUpdateStatus()
                }
                .onChange(of: scenePhase) { _, phase in
                    if phase == .active {
                        Task {
                            await store.refresh()
                            await store.refreshUpdateStatus()
                        }
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
