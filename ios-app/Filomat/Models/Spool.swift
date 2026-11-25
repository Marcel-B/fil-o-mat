import Foundation

/// Eine angebrochene Spule einer Sorte (Item). Die Nummer steht mit Stift auf der Spule,
/// das Restgewicht bucht das Backend beim Drucken ab.
struct Spool: Identifiable, Codable, Hashable {
    var id: String
    var itemId: String
    var number: Int
    var remainingWeight: Double?
    /// Drucker, in dem die Spule steckt; `nil` heißt Lager.
    var printerId: String?
    /// Kopf bzw. Slot am Drucker (0-basiert).
    var tool: Int?
    /// Container, in dem die Spule im Lager liegt; ältere Backends liefern den Wert nicht.
    var containerId: String?

    var place: SpoolPlace { SpoolPlace(printerId: printerId, tool: printerId == nil ? nil : tool ?? 0, containerId: printerId == nil ? containerId : nil) }

    /// "U1 Kopf 2", "Prusa", "Regal oben" oder "Lager".
    func locationText(printers: [Printer], containers: [Container] = []) -> String {
        place.text(printers: printers, containers: containers)
    }
}

/// Platz einer angebrochenen Spule: ein Kopf eines Druckers oder das Lager, dort optional ein Container.
struct SpoolPlace: Hashable {
    var printerId: String?
    var tool: Int?
    var containerId: String?

    static let storage = SpoolPlace()

    static func printer(_ id: String, tool: Int) -> SpoolPlace { SpoolPlace(printerId: id, tool: tool) }

    static func container(_ id: String) -> SpoolPlace { SpoolPlace(containerId: id) }

    func text(printers: [Printer], containers: [Container] = []) -> String {
        if let printerId {
            guard let printer = printers.first(where: { $0.id == printerId }) else { return String(localized: "label.printer", defaultValue: "Drucker") }
            return printer.placeName(tool: tool)
        }
        if let containerId, let container = containers.first(where: { $0.id == containerId }) {
            return container.name
        }
        return String(localized: "Lager")
    }
}

/// Anfrage für `PUT /api/spools/{id}` und `POST /api/items/{id}/spools`.
struct SpoolUpdate: Encodable {
    var remainingWeight: Double?
    var printerId: String?
    var tool: Int?
    var containerId: String?
}
