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

    private static string Job(string state, double progress, string meta, int? remaining = null) => $$$"""
        {"id":17,"state":"{{{state}}}","progress":{{{progress}}},"time_printing":1200,{{{(remaining is { } r ? $"\"time_remaining\":{r}," : "")}}}
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

    [Fact]
    public async Task BooksAPrintAsCompletedWhenItDisappearsAfterItsRemainingTime()
    {
        var printer = await CreatePrinterAsync("Tifa");
        var (item, _) = await PlaceSpoolAsync("T1", printer.Id, 0);

        // Letzter Abgleich kurz vor Schluss, danach ist der Druck weg (Platte entnommen oder Drucker aus)
        const string meta = """{"filament used [g]":20.0,"filament used [mm]":6700,"filament_type":"PLA"}""";
        _printer.Routes["/api/v1/job"] = Job("PRINTING", 97, meta, remaining: 40);
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = "";
        _printer.Routes["/api/v1/status"] = """{"printer":{"state":"IDLE"}}""";
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        var entry = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("completed", entry.Status);
        Assert.Equal(980, await RemainingAsync(item.Id));
    }

    [Fact]
    public async Task BooksAPrintAsCompletedWhenThePrinterStillShowsFinished()
    {
        var printer = await CreatePrinterAsync("MK4S");
        await PlaceSpoolAsync("T2", printer.Id, 0);

        const string meta = """{"filament used [g]":20.0,"filament used [mm]":6700,"filament_type":"PLA"}""";
        _printer.Routes["/api/v1/job"] = Job("PRINTING", 80, meta, remaining: 1800);
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = "";
        _printer.Routes["/api/v1/status"] = """{"printer":{"state":"FINISHED"}}""";
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        var entry = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("completed", entry.Status);
    }

    [Fact]
    public async Task KeepsAPrintCancelledThatDisappearsLongBeforeItsEnd()
    {
        var printer = await CreatePrinterAsync("MK4S");
        await PlaceSpoolAsync("T3", printer.Id, 0);

        const string meta = """{"filament used [g]":20.0,"filament used [mm]":6700,"filament_type":"PLA"}""";
        _printer.Routes["/api/v1/job"] = Job("PRINTING", 97, meta, remaining: 1800);
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = "";
        _printer.Routes["/api/v1/status"] = """{"printer":{"state":"IDLE"}}""";
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        var entry = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal("cancelled", entry.Status);
    }

    /// <summary>Binär-G-Code wie aus PrusaSlicer: Dateikopf, Datei- und Druckermetadaten, Vorschaubild, G-Code, mit CRC32.</summary>
    private static byte[] Bgcode(string printerMetadata)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("GCDE"u8);
        writer.Write(1u);
        writer.Write((ushort)1);
        void Block(ushort type, ushort compression, byte[] parameters, byte[] data)
        {
            writer.Write(type);
            writer.Write(compression);
            writer.Write((uint)(compression == 0 ? data.Length : data.Length * 3));
            if (compression != 0) writer.Write((uint)data.Length);
            writer.Write(parameters);
            writer.Write(data);
            writer.Write(0xDEADBEEFu);
        }
        Block(0, 0, [0, 0], "Producer=PrusaSlicer 2.9.2\n"u8.ToArray());
        Block(3, 0, [0, 0], System.Text.Encoding.UTF8.GetBytes(printerMetadata));
        Block(5, 0, [2, 0, 16, 0, 16, 0], new byte[700]);
        Block(1, 3, [1, 0], new byte[300]);
        return stream.ToArray();
    }

    /// <summary>So meldet die Buddy-Firmware (MK4S, Core One) einen Druck: ohne Metadaten bei Job und Datei.</summary>
    private static string BuddyJob(string state, double progress) => $$$"""
        {"id":42,"state":"{{{state}}}","progress":{{{progress}}},"time_remaining":600,"time_printing":3600,
         "file":{"refs":{"icon":"/thumb/s/usb/BOX~1.BGC","thumbnail":"/thumb/l/usb/BOX~1.BGC","download":"/usb/BOX~1.BGC"},
                 "name":"BOX~1.BGC","display_name":"box_0.4n_0.2mm_PLA_MK4S_1h.bgcode","path":"/usb","size":123456,"m_timestamp":1763000000}}
        """;

    [Fact]
    public async Task ReadsTheUsageFromTheBgcodeFileWhenPrusaLinkHasNoMetadata()
    {
        var printer = await CreatePrinterAsync("Tifa");
        var (item, _) = await PlaceSpoolAsync("T4", printer.Id, 0);

        _printer.Routes["/api/v1/job"] = BuddyJob("PRINTING", 30);
        _printer.Routes["/api/v1/files/usb/BOX~1.BGC"] = """{"name":"BOX~1.BGC","ro":false,"type":"PRINT_FILE","display_name":"box.bgcode"}""";
        _printer.Files["/usb/BOX~1.BGC"] = Bgcode(
            "printer_model=MK4S\nfilament_type=PLA\nnozzle_diameter=0.4\nfilament used [mm]=13800.50\nfilament used [g]=41.20\n");
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = BuddyJob("FINISHED", 100);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        var entry = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal(41.2, entry.Grams);
        Assert.Equal(958.8, await RemainingAsync(item.Id));
    }

    [Fact]
    public async Task ReadsTheUsagePerMmuSlotFromTheBgcodeFile()
    {
        _printer.Routes["/api/v1/info"] = """{"mmu":true,"name":"Core One"}""";
        var printer = await CreatePrinterAsync("Core One");
        var (white, _) = await PlaceSpoolAsync("W2", printer.Id, 0);
        var (black, _) = await PlaceSpoolAsync("B2", printer.Id, 2);

        _printer.Routes["/api/v1/job"] = BuddyJob("PRINTING", 30);
        _printer.Files["/usb/BOX~1.BGC"] = Bgcode(
            "filament_type=PLA;PLA;PETG;PLA;PLA\nfilament used [mm]=3350.00, 0.00, 1675.00, 0.00, 0.00\nfilament used [g]=10.00, 0.00, 5.00, 0.00, 0.00\n");
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = BuddyJob("FINISHED", 100);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(990, await RemainingAsync(white.Id));
        Assert.Equal(995, await RemainingAsync(black.Id));
    }

    [Fact]
    public async Task LooksTheUsageUpAgainForAPrintTrackedWithoutIt()
    {
        var printer = await CreatePrinterAsync("Tifa");
        var (item, _) = await PlaceSpoolAsync("T5", printer.Id, 0);

        // Erster Abgleich ohne lesbare Datei (wie vor dem Update), danach ist sie da
        _printer.Routes["/api/v1/job"] = BuddyJob("PRINTING", 30);
        await SyncAsync(printer.Id);
        _printer.Files["/usb/BOX~1.BGC"] = Bgcode("filament used [mm]=6700\nfilament used [g]=20\n");
        await SyncAsync(printer.Id);
        _printer.Routes["/api/v1/job"] = BuddyJob("FINISHED", 100);
        Assert.Equal(1, (await SyncAsync(printer.Id)).Recorded);

        Assert.Equal(980, await RemainingAsync(item.Id));
    }

    [Fact]
    public async Task IgnoresPlainGcodeAndUnknownData()
    {
        Assert.Null(await BgcodeMetadata.ReadAsync(new MemoryStream("; generated by PrusaSlicer\nG28\n"u8.ToArray()), default));
        Assert.Null(await BgcodeMetadata.ReadAsync(new MemoryStream(Bgcode("printer_model=MK4S\n")), default));
        var values = await BgcodeMetadata.ReadAsync(new MemoryStream(Bgcode("filament used [g]=1.5\n")), default);
        Assert.Equal("1.5", values!["filament used [g]"]);
    }

    [Theory]
    [InlineData(99.6, null, 0, true)]
    [InlineData(97, 100, 120, true)]
    [InlineData(97, 600, 120, false)]
    [InlineData(60, 3 * 3600, 24 * 3600, false)]
    [InlineData(90, 600, 900, true)]
    [InlineData(97, null, 3600, false)]
    public void DecidesFromProgressAndRemainingTime(double progress, int? remaining, int secondsLater, bool finished)
    {
        var seen = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var job = new PrusaLinkAdapter.TrackedJob("1", "x", progress, null, null, null, null, null, null, remaining, seen);
        Assert.Equal(finished, PrusaLinkAdapter.LooksFinished(job, seen.AddSeconds(secondsLater)));
    }
}
