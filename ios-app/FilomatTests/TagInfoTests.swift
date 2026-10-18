import XCTest
@testable import Filomat

final class TagInfoTests: XCTestCase {
    func testFieldsKeepKnownOrderAndUnknownKeys() throws {
        let json = #"{"zeta":"z","brand":"Prusament","protocol":"openspool","subtype":"Matte","min_temp":210,"type":"PLA","extra":[1,2],"flag":true}"#
        let fields = try XCTUnwrap(TagInfo.fields(fromJSON: Data(json.utf8)))
        XCTAssertEqual(fields.map(\.key), ["protocol", "brand", "type", "subtype", "min_temp", "extra", "flag", "zeta"])
        XCTAssertEqual(fields.first { $0.key == "subtype" }?.label, "Untertyp")
        XCTAssertEqual(fields.first { $0.key == "min_temp" }?.value, "210")
        XCTAssertEqual(fields.first { $0.key == "extra" }?.value, "1, 2")
        XCTAssertEqual(fields.first { $0.key == "flag" }?.value, "ja")
        XCTAssertEqual(fields.first { $0.key == "zeta" }?.label, "zeta")
    }

    func testFieldsRejectNonObjects() {
        XCTAssertNil(TagInfo.fields(fromJSON: Data("[1]".utf8)))
        XCTAssertNil(TagInfo.fields(fromJSON: Data("kaputt".utf8)))
    }

    func testRecordPayloadText() {
        // Text-Record: Statusbyte mit Sprachlänge 2, "de", dann der Text
        let text = TagRecord(format: 1, type: "T", payload: Data([0x02]) + Data("deHallo".utf8))
        XCTAssertEqual(text.payloadText, "Hallo")
        let uri = TagRecord(format: 1, type: "U", payload: Data([0x04]) + Data("example.com".utf8))
        XCTAssertEqual(uri.payloadText, "https://example.com")
        let binary = TagRecord(format: 4, type: "x", payload: Data([0xFF, 0x00, 0xFE]))
        XCTAssertEqual(binary.payloadText, "FF 00 FE")
        XCTAssertTrue(TagRecord(format: 2, type: "application/json", payload: Data()).isJSON)
    }
}
