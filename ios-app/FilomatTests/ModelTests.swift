import XCTest
@testable import Filomat

final class ModelTests: XCTestCase {
    func testHexColorParsing() {
        XCTAssertEqual(HexColor("FF0000"), HexColor("#ff0000"))
        XCTAssertEqual(HexColor(" #00FF00 ")?.green, 1)
        XCTAssertEqual(HexColor("80FFFFFF")?.alpha ?? 0, 128.0 / 255, accuracy: 0.001)
        XCTAssertNil(HexColor("FFF"))
        XCTAssertNil(HexColor("GGGGGG"))
        XCTAssertNil(HexColor(""))
        XCTAssertEqual(HexColor.stored(" #FF0000 "), "FF0000")
        XCTAssertNil(HexColor.stored("  "))
    }

    func testDisplayTitle() {
        var item = Item(rfidTag: "A", name: "Fallback")
        XCTAssertEqual(item.displayTitle, "Fallback")
        item.itemData = ItemJsonData(type: "PLA", brand: "Prusament", variant: "Silk")
        XCTAssertEqual(item.displayTitle, "Prusament PLA Silk")
    }

    func testDecodesOpenSpoolJSONWithNumbersAndUnknownKeys() throws {
        let json = #"{"protocol":"openspool","version":"1.0","type":"PLA","color_hex":"FFAABB","brand":"Generic","min_temp":190,"max_temp":"220","extra":true}"#
        let data = try JSONDecoder().decode(ItemJsonData.self, from: Data(json.utf8))
        XCTAssertEqual(data.protocolName, "openspool")
        XCTAssertEqual(data.minTemp, "190")
        XCTAssertEqual(data.maxTemp, "220")
        XCTAssertEqual(data.colorHex, "FFAABB")
        XCTAssertNil(data.bedMinTemp)
    }

    func testEncodesSnakeCaseKeysWithoutNulls() throws {
        let data = ItemJsonData(protocolName: "openspool", type: "PLA", colorHex: "FF0000")
        let json = String(decoding: try TagJSON.encoder.encode(data), as: UTF8.self)
        XCTAssertTrue(json.contains(#""protocol":"openspool""#))
        XCTAssertTrue(json.contains(#""color_hex":"FF0000""#))
        XCTAssertFalse(json.contains("null"))
    }

    func testPresetLookupFallsBackToGenericPreset() {
        let db = FilamentPresetDatabase(
            presets: [
                FilamentPreset(type: "PLA", brand: nil, minTemp: "190", maxTemp: "220"),
                FilamentPreset(type: "PLA", brand: "Prusament", minTemp: "200", maxTemp: "230"),
            ],
            brands: ["Sunlu", "Prusament", "Sunlu"],
            variants: []
        )
        XCTAssertEqual(db.preset(type: "PLA", brand: "Prusament")?.minTemp, "200")
        XCTAssertEqual(db.preset(type: "PLA", brand: "Unbekannt")?.minTemp, "190")
        XCTAssertNil(db.preset(type: "ABS", brand: nil))
        XCTAssertEqual(db.brands, ["Prusament", "Sunlu"])
    }

    func testBundledPresetsLoad() {
        let db = FilamentPresetDatabase()
        XCTAssertNotNil(db.preset(type: "PLA", brand: nil))
        XCTAssertFalse(db.brands.isEmpty)
        XCTAssertFalse(db.variants.isEmpty)
    }
}
