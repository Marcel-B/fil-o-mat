import Foundation

/// Ein Lagerort (Box, Regal, Drucker …) mit RFID-Tag.
/// Die enthaltenen Items werden nicht hier gespeichert, sondern über `Item.containerId` zugeordnet.
struct Container: Identifiable, Codable, Hashable {
    static let printerId = "standard_drucker"
    static let looseId = "standard_lose"

    var id: String = UUID().uuidString
    var rfidTag: String
    var name: String
    var description: String = ""
    var capacity: Int?
    var createdAt: Date = Date()
    var isStandard: Bool = false

    static let standardContainers: [Container] = [
        Container(
            id: printerId,
            rfidTag: "STANDARD_DRUCKER",
            name: "Drucker",
            description: "Standard-Container für Drucker",
            isStandard: true
        ),
        Container(
            id: looseId,
            rfidTag: "STANDARD_LOSE",
            name: "Lose",
            description: "Standard-Container für lose Items",
            isStandard: true
        ),
    ]
}

/// Daten, die beim Anlegen eines Containers auf dessen Tag geschrieben werden.
struct ContainerTagData: Codable {
    var name: String
    var description: String
    var capacity: String
}
