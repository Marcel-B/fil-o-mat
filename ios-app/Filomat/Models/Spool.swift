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

    /// "U1 Kopf 2", "Prusa" oder "Lager".
    func locationText(printers: [Printer]) -> String {
        guard let printerId else { return "Lager" }
        guard let printer = printers.first(where: { $0.id == printerId }) else { return "Drucker" }
        return printer.placeName(tool: tool)
    }
}

/// Anfrage für `PUT /api/spools/{id}`.
struct SpoolUpdate: Encodable {
    var remainingWeight: Double?
    var printerId: String?
    var tool: Int?
}
