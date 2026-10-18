import SwiftUI

struct ScanView: View {
    @Environment(ContainerStore.self) private var store
    @State private var model = ScanViewModel()
    @State private var isScanning = false

    var body: some View {
        @Bindable var model = model

        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Picker("Modus", selection: Binding(get: { model.scanMode }, set: { model.setScanMode($0) })) {
                    ForEach(ScanMode.allCases) { mode in
                        Text(mode.title).tag(mode)
                    }
                }
                .pickerStyle(.segmented)

                VStack(alignment: .leading, spacing: 8) {
                    Text(model.stepTitle).font(.headline)
                    Text(model.stepDescription).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding()
                .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))

                if NFCService.isAvailable {
                    Button {
                        Task {
                            isScanning = true
                            await model.scan(using: .shared, store: store)
                            isScanning = false
                        }
                    } label: {
                        HStack {
                            if isScanning { ProgressView() }
                            Text(isScanning ? "Scanne..." : "Tag scannen")
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(isScanning)
                } else {
                    Text("NFC ist auf diesem Gerät nicht verfügbar.").foregroundStyle(.red)
                }

                if let container = model.scannedContainer {
                    ContainerStatusCard(container: container)
                }
                if let item = model.scannedItem {
                    ItemStatusCard(item: item)
                }
                if let warning = model.warningMessage {
                    Text(warning)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding()
                        .background(Color.red.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
                }
                if let error = model.errorMessage {
                    Text(error).foregroundStyle(.red)
                }
            }
            .padding()
        }
        .safeAreaInset(edge: .bottom) {
            if model.canPerformAction {
                Button {
                    model.performAction(store: store)
                } label: {
                    Text(model.actionTitle).frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .padding()
                .background(.bar)
            }
        }
        .navigationTitle("Scannen")
        .alert("Filament umlagern?", isPresented: $model.showConfirmMove) {
            Button("Ja, umlagern") { model.confirmMove() }
            Button("Abbrechen", role: .cancel) { model.cancelMove() }
        } message: {
            Text("Dieses Filament ist bereits in einem anderen Container. Möchten Sie es in den neuen Container umlagern?")
        }
    }
}
