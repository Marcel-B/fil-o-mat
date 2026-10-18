using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;

namespace Filomat.Api.Tests;

/// <summary>PrusaLink ohne Historie: der Druck wird beim Abgleich verfolgt und nach dem Ende gebucht.</summary>
public class PrusaLinkTests : IDisposable
{
    private readonly FakeMoonraker _printer = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PrusaLinkTests()
    {
        _factory = new ApiFactory { PrinterHandler = _printer };
        _client = _factory.CreateClient();
        _printer.Routes["/api/v1/info"] = """{"mmu":false,"name":"MK4","nozzle_diameter":0.4}""";
        _printer.Routes["/api/v1/job"] = "";
    }

    public void Dispose() => _factory.Dispose();

    private static string Job(string state, double progress, string meta) => $$$"""
        {"id":17,"state":"{{{state}}}","progress":{{{progress}}},"time_printing":1200,
         "file":{"name":"BOX~1.BGC","display_name":"box_0.4n_0.2mm_PLA_COREONE_1h.bgcode","path":"/usb","m_timestamp":1760000000,
                 "meta":{{{meta}}}}}
        """;

    private async Task<PrinterResponse> CreatePrinterAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest(name, PrinterType.PrusaLink, "http://prusa.local", "abc123", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<(Item Item, Spool Spool)> PlaceSpoolAsync(string tag, string printerId, int tool)
    {
        var item = (await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, tag, "Prusament PLA", null, null, new FilamentData { Type = "PLA" }, 1000))).Content.ReadFromJsonAsync<Item>())!;
        var spool = (await (await _client.PostAsJsonAsync($"/api/items/{item.Id}/spools",
            new OpenSpoolRequest(null, printerId, tool))).Content.ReadFromJsonAsync<Spool>())!;
        return (item, spool);
    }

    private async Task<SyncResponse> SyncAsync(string printerId) =>
        (await (await _client.PostAsync($"/api/printers/{printerId}/sync", null)).Content.ReadFromJsonAsync<SyncResponse>())!;

    private async Task<double?> RemainingAsync(string itemId) =>
        Assert.Single((await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?itemId={itemId}"))!).RemainingWeight;

    [Fact]
    public async Task BooksAFinishedPrintOnTheSpoolInThePrinter()
    {
        var printer = await CreatePrinterAsync("MK4");
        Assert.Equal(1, printer.Tools);
        var (item, _) = await PlaceSpoolAsync("P1", printer.Id, 0);

        const string meta = """{"filament used [g]":20.5,"filament used [mm]":6870.2,"filament_type":"PLA"}""";
        _printer.Routes["/api/v1/job"] = Job("PRINTING", 40, meta);
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);
        _printer.Routes["/api/v1/job"] = Job("FINISHED", 100, meta);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);
        // Der beendete Druck wird nicht noch einmal gebucht
        Assert.Equal(0, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(979.5, await RemainingAsync(item.Id));
        Assert.All(_printer.Requests, r => Assert.Equal("abc123", r.Headers.GetValues("X-Api-Key").Single()));
    }

    [Fact]
    public async Task BooksEachMmuSlotProRataWhenThePrintDisappears()
    {
        _printer.Routes["/api/v1/info"] = """{"mmu":true,"name":"Core One"}""";
        var printer = await CreatePrinterAsync("Core One");
        Assert.Equal(PrusaLinkAdapter.MmuSlots, printer.Tools);
        var (white, _) = await PlaceSpoolAsync("W", printer.Id, 1);
        var (black, _) = await PlaceSpoolAsync("B", printer.Id, 3);

        // Ohne Slicer-Angaben im Job werden sie bei der Datei nachgeschlagen
        _printer.Routes["/api/v1/job"] = Job("PRINTING", 50, "null");
        _printer.Routes["/api/v1/files/usb/BOX~1.BGC"] = """
            {"name":"BOX~1.BGC","meta":{"filament used [g]":16.0,"filament used [mm]":5360,
             "filament used [g] per tool":[0,12.5,0,3.5,0],"filament used [mm] per tool":[0,4190,0,1170,0]}}
            """;
        await SyncAsync(printer.Id);
        // Danach meldet der Drucker keinen Druck mehr: abgebrochen bei 50 %
        _printer.Routes["/api/v1/job"] = "";
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(993.75, await RemainingAsync(white.Id));
        Assert.Equal(998.25, await RemainingAsync(black.Id));
        var entries = (await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!;
        Assert.Equal([1, 3], entries.Select(e => e.Tool).Order());
        Assert.All(entries, e => Assert.Equal("cancelled", e.Status));
    }
}
