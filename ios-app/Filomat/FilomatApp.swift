import SwiftUI

@main
struct FilomatApp: App {
    @State private var store = ContainerStore()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(store)
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
        }
    }
}
