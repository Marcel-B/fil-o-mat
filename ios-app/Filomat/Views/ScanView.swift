import SwiftUI

/// Scannen-Tab: eine Liste der Dinge, die man mit einem Tag tun kann. Jede führt in einen eigenen Ablauf.
struct ScanView: View {
    @Environment(ContainerStore.self) private var store

    var body: some View {
        List {
            Section("Nachsehen") {
                NavigationLink {
                    TagInfoView()
                } label: {
                    ScanActionRow(title: "Filament-Tag lesen", detail: "Alles, was auf dem Tag steht, und was Fil-o-mat zur Sorte weiß",
                                  systemImage: "info.circle")
                }
                NavigationLink {
                    ContainerScanView()
                } label: {
                    ScanActionRow(title: "Container ansehen", detail: "Container scannen und sehen, was darin liegt",
                                  systemImage: "archivebox")
                }
            }

            Section {
                NavigationLink {
                    PlaceFlowView(target: .container)
                } label: {
                    ScanActionRow(title: "Einlagern", detail: "Filament scannen, dann den Container",
                                  systemImage: "tray.and.arrow.down")
                }
                NavigationLink {
                    PlaceFlowView(target: .printer)
                } label: {
                    ScanActionRow(title: "In Drucker einlegen", detail: "Filament scannen, dann Drucker und Kopf wählen",
                                  systemImage: "printer")
                }
                .disabled(store.backend == nil)
            } header: {
                Text("Filament bewegen")
            } footer: {
                if store.backend == nil {
                    Text("Spulen in Druckern verwaltet das Backend. Verbinde es über das Wolken-Symbol in der Container-Liste.")
                }
            }

            Section("Vorrat") {
                NavigationLink {
                    StockView()
                } label: {
                    ScanActionRow(title: "Vorrat", detail: "Volle und angebrochene Spulen je Sorte, was nachgekauft werden sollte",
                                  systemImage: "shippingbox")
                }
                NavigationLink {
                    TransmissionScanView()
                } label: {
                    ScanActionRow(title: "Lichtdurchlässigkeit erfassen", detail: "Musterplättchen scannen und den TD-Wert eintragen",
                                  systemImage: "sun.max")
                }
            }
        }
        .navigationTitle("Scannen")
    }
}

private struct ScanActionRow: View {
    var title: LocalizedStringKey
    var detail: LocalizedStringKey
    var systemImage: String

    var body: some View {
        Label {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
        } icon: {
            Image(systemName: systemImage).foregroundStyle(.tint)
        }
        .padding(.vertical, 2)
    }
}
