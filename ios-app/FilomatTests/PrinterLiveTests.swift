import XCTest
@testable import Filomat

final class PrinterLiveTests: XCTestCase {
    /// Antwort von `/api/printers/status`, wie sie das Backend liefert (gekürzt).
    private let response = """
    [{"printerId":"p1","name":"Kobra X","type":"Anycubic","updatedAt":"2026-10-24T18:40:12.1234567Z",
      "status":{"state":"Printing","jobName":"benchy","jobKey":"614707220","progress":45.5,"remainingSeconds":1920,
        "elapsedSeconds":600,"layer":12,"totalLayers":80,"activeTool":0,
        "heaters":[{"name":"Düse","tool":0,"actual":214.6,"target":215},{"name":"Bett","tool":null,"actual":59.8,"target":60}],
        "slots":[{"tool":0,"loaded":true,"type":"PLA","colorHex":"000000","remainPercent":null,"tagUid":null},
                 {"tool":1,"loaded":false,"type":null,"colorHex":null,"remainPercent":null,"tagUid":null}],
        "dryers":[{"name":"ACE 1","humidityPercent":52,"humidityLevel":null,"temperature":31,"tools":[0,1,2,3]}],
        "jobGramsPerTool":[40,0],"jobGramsEstimated":true,"message":null,"hasThumbnail":false,"isActive":true},
      "tools":[{"tool":0,"place":"Slot 1","spoolId":"s1","spoolNumber":2,"itemId":"i1","itemName":"PLA Schwarz","type":"PLA",
                "colorHex":"000000","remainingGrams":12,"reported":{"tool":0,"loaded":true,"type":"PLA","colorHex":"000000"},
                "neededGrams":21.8,"enough":false},
               {"tool":1,"place":"Slot 2","spoolId":null,"spoolNumber":null,"itemId":null,"itemName":null,"type":null,
                "colorHex":null,"remainingGrams":null,"reported":{"tool":1,"loaded":false},"neededGrams":null,"enough":null}],
      "hints":[{"kind":"SpoolShort","warning":true,"tool":0,"text":"Slot 1: PLA Schwarz #2 hat noch 12 g, der Druck braucht geschätzt noch 22 g."},
               {"kind":"Humidity","warning":false,"tool":null,"text":"ACE 1: 52 % Feuchte, das ist viel für Filament."}]}]
    """

    func testDecodesTheBackendStatus() throws {
        let live = try XCTUnwrap(BackendClient.decoder.decode([PrinterLive].self, from: Data(response.utf8)).first)
        XCTAssertEqual(live.type, .anycubic)
        XCTAssertNotNil(live.updatedDate)
        XCTAssertTrue(live.status.isActive)
        XCTAssertEqual(live.status.summary, "Druckt benchy · 45 % · noch 32 min")
        XCTAssertEqual(live.status.heaters?.first?.text, "215 °C / 215 °C")
        XCTAssertEqual(live.status.dryers?.first?.text, "52 % Feuchte · 31 °C")
        XCTAssertEqual(live.tools[0].title, "PLA Schwarz #2")
        XCTAssertEqual(live.tools[1].title, "leer")
        XCTAssertEqual(live.tools[0].enough, false)
        XCTAssertEqual(live.warnings.count, 1)
        XCTAssertEqual(live.hints[1].symbol, "humidity")
    }

    func testFormatsDurationsAndWeights() {
        XCTAssertEqual(PrinterStatus.duration(4980), "1:23 h")
        XCTAssertEqual(PrinterStatus.duration(30), "1 min")
        XCTAssertEqual(340.4.gramsText, "340 g")
    }

    func testMonthLabelsStayUniqueAcrossYears() {
        let month = StatsResponse.Month(month: "2026-10", grams: 0, jobs: 0, printSeconds: 0)
        XCTAssertEqual(month.yearLabel, "10/26")
    }
}
