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
                {"VENDOR":"Sunlu","MAIN_TYPE":"PLA","CARD_UID":[4,161,59,82,203,42,129]},
                {"VENDOR":"Sunlu","MAIN_TYPE":"PLA","CARD_UID":[4,234,22,84,203,42,129]},
                {"VENDOR":"Overture","MAIN_TYPE":"TPU","CARD_UID":[4,152,245,83,203,42,129]},
                {"VENDOR":"NONE","MAIN_TYPE":"NONE","CARD_UID":[4,89,56,81,203,42,129]}],
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
        var u1 = await CreateU1Async();
        var black = await CreateSpoolAsync("04EA1654CB2A81", 1000);   // Kopf 2 (Index 1)
        var white = await CreateSpoolAsync("04A13B52CB2A81", 1000);   // Kopf 1, im Druck nicht benutzt

        var result = await (await _client.PostAsync($"/api/printers/{u1.Id}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Null(result!.Error);
        Assert.Equal(1, result.Recorded);

        // 20,73 g geplant, 7092,2 von 7105,0 mm gedruckt
        Assert.Equal(979.31, (await _client.GetFromJsonAsync<Item>($"/api/items/{black.Id}"))!.RemainingWeight);
        Assert.Null((await _client.GetFromJsonAsync<Item>($"/api/items/{white.Id}"))!.RemainingWeight);

        var entries = await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={u1.Id}");
        Assert.Equal(2, entries!.Count);
        var unknown = Assert.Single(entries, c => c.ItemId is null);
        Assert.Equal(3, unknown.Tool);
        Assert.Equal("04593851CB2A81", unknown.TagUid);
        Assert.Equal(0.46, unknown.Grams);

        // Erkannte Spulen liegen jetzt im Drucker-Container
        Assert.Equal(u1.ContainerId, (await _client.GetFromJsonAsync<Item>($"/api/items/{white.Id}"))!.ContainerId);

        var again = await (await _client.PostAsync($"/api/printers/{u1.Id}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(0, again!.Recorded);
    }
}
