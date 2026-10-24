using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;

namespace Filomat.Api.Tests;

/// <summary>Antworten wie von Marcels U1 mit paxx12-Firmware (gekürzt).</summary>
public class SnapmakerU1Tests : IDisposable
{
    private readonly FakeMoonraker _printer = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SnapmakerU1Tests()
    {
        _factory = new ApiFactory { PrinterHandler = _printer };
        _client = _factory.CreateClient();
        _printer.Routes["/printer/objects/query"] = """
            {"result":{"eventtime":731304.1,"status":{
              "filament_detect":{"info":[
                {"VENDOR":"Sunlu","MAIN_TYPE":"PLA","SUB_TYPE":"","RGB_1":16777215,"HOTEND_MIN_TEMP":190,"HOTEND_MAX_TEMP":220,"BED_TEMP":60,"CARD_UID":[4,161,59,82,203,42,129]},
                {"VENDOR":"Sunlu","MAIN_TYPE":"PLA","SUB_TYPE":"","RGB_1":0,"HOTEND_MIN_TEMP":190,"HOTEND_MAX_TEMP":220,"BED_TEMP":60,"CARD_UID":[4,234,22,84,203,42,129]},
                {"VENDOR":"Overture","MAIN_TYPE":"TPU","SUB_TYPE":"","RGB_1":0,"HOTEND_MIN_TEMP":210,"HOTEND_MAX_TEMP":230,"BED_TEMP":50,"CARD_UID":[4,152,245,83,203,42,129]},
                {"VENDOR":"NONE","MAIN_TYPE":"NONE","SUB_TYPE":"NONE","RGB_1":16777215,"HOTEND_MIN_TEMP":0,"CARD_UID":[4,89,56,81,203,42,129]}],
               "state":[0,0,0,0]},
              "print_task_config":{"extruder_map_table":[0,1,2,3,0,0,0,0]}}}}
            """;
        _printer.Routes["/server/history/list"] = History(DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
    }

    public void Dispose() => _factory.Dispose();

    private static string History(double endTime) => $$$"""
        {"result":{"count":1,"jobs":[{"job_id":"0001E6","filename":"frontplatte-einlage_PLA_52m26s.gcode","status":"completed",
          "start_time":{{{(endTime - 3400).ToString(CultureInfo.InvariantCulture)}}},"end_time":{{{endTime.ToString(CultureInfo.InvariantCulture)}}},
          "filament_used":7092.2114499967065,
          "metadata":{"slicer":"OrcaSlicer","filament_type":"PLA;PLA;TPU;PLA",
            "filament_weight":[0.0,20.73,0.0,0.46],"filament_total":7105.01,"filament_weight_total":21.19,
            "filament_used_mm":[0.0,6951.39,0.0,153.62]}}]}}
        """;

    private async Task<PrinterResponse> CreateU1Async()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Snapmaker U1", PrinterType.SnapmakerU1, "http://192.168.2.155", null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<Item> CreateSpoolAsync(string tag, double? net)
    {
        var response = await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, tag, "Sunlu PLA", null, Container.LooseId,
            new FilamentData { Type = "PLA" }, net));
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    [Fact]
    public async Task BooksEachToolheadOnTheSpoolTheU1Detected()
    {
        var black = await CreateSpoolAsync("04EA1654CB2A81", 1000);   // Kopf 2 (Index 1)
        var white = await CreateSpoolAsync("04A13B52CB2A81", 1000);   // Kopf 1, im Druck nicht benutzt
        var u1 = await CreateU1Async();                               // gleicht beim Anlegen sofort ab

        var printer = await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{u1.Id}");
        Assert.Null(printer!.LastSyncError);

        // 20,73 g geplant, 7092,2 von 7105,0 mm gedruckt
        var blackSpool = Assert.Single((await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?itemId={black.Id}"))!);
        Assert.Equal((979.31, 1), (blackSpool.RemainingWeight!.Value, blackSpool.Tool!.Value));
        var whiteSpool = Assert.Single((await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?itemId={white.Id}"))!);
        Assert.Equal((1000d, 0), (whiteSpool.RemainingWeight!.Value, whiteSpool.Tool!.Value));

        var entries = await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={u1.Id}");
        Assert.Equal(2, entries!.Count);
        var head4 = Assert.Single(entries, c => c.Tool == 3);
        Assert.Equal("04593851CB2A81", head4.TagUid);
        Assert.Equal(0.46, head4.Grams);

        var again = await (await _client.PostAsync($"/api/printers/{u1.Id}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(0, again!.Recorded);
    }

    [Fact]
    public async Task BooksOnTheSpoolPlacedInTheAppWhenTheHeadHasNoTag()
    {
        // Kopf 2 erkennt kein Tag (Plättchen nicht eingelegt); die Spule wurde in der App dorthin gelegt
        _printer.Routes["/printer/objects/query"] = _printer.Routes["/printer/objects/query"]
            .Replace("[4,234,22,84,203,42,129]", "[0,0,0,0,0,0,0]");
        var history = _printer.Routes["/server/history/list"];
        _printer.Routes["/server/history/list"] = """{"result":{"count":0,"jobs":[]}}""";
        var u1 = await CreateU1Async();

        var jayo = await CreateSpoolAsync("04BEEF00CB2A81", 1000);
        var opened = await _client.PostAsJsonAsync($"/api/items/{jayo.Id}/spools", new OpenSpoolRequest(1100, u1.Id, 1));
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);

        _printer.Routes["/server/history/list"] = history;
        var sync = await (await _client.PostAsync($"/api/printers/{u1.Id}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(1, sync!.Recorded);

        var spool = Assert.Single((await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?itemId={jayo.Id}"))!);
        Assert.Equal((1079.31, 1), (spool.RemainingWeight!.Value, spool.Tool!.Value));
        var entries = await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={u1.Id}");
        var head2 = Assert.Single(entries!, c => c.Tool == 1);
        Assert.Equal((jayo.Id, spool.Id), (head2.ItemId, head2.SpoolId));
        Assert.Null(head2.TagUid);
    }

    [Fact]
    public async Task TakesOverAllSpoolsTheU1Detected()
    {
        var u1 = await CreateU1Async();

        var loaded = await _client.GetFromJsonAsync<List<Item>>($"/api/containers/{u1.ContainerId}/items");
        Assert.Equal(4, loaded!.Count);

        var tpu = loaded.Single(i => i.RfidTag == "0498F553CB2A81");
        Assert.Equal("Overture TPU", tpu.Name);
        Assert.Equal("TPU", tpu.ItemData!.Type);
        Assert.Equal("Overture", tpu.ItemData.Brand);
        Assert.Equal("000000", tpu.ItemData.ColorHex);
        Assert.Equal("210", tpu.ItemData.MinTemp);
        Assert.Equal("FFFFFF", loaded.Single(i => i.RfidTag == "04A13B52CB2A81").ItemData!.ColorHex);
        Assert.Equal("Spule aus Kopf 4", loaded.Single(i => i.RfidTag == "04593851CB2A81").Name);

        // Der Druck auf Kopf 4 hängt jetzt an der neu angelegten Spule
        var head4 = loaded.Single(i => i.RfidTag == "04593851CB2A81");
        var entries = await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?itemId={head4.Id}");
        Assert.Single(entries!);
    }
}
