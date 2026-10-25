using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;

namespace Filomat.Api.Tests;

/// <summary>NFC-Leser am Drucker (ESP32): Musterplättchen pro Slot legen die Spule ihrer Sorte dort ein.</summary>
public class ReaderTests : IDisposable
{
    private readonly FakeMoonraker _printer = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ReaderTests()
    {
        _factory = new ApiFactory { PrinterHandler = _printer };
        _client = _factory.CreateClient();
        _printer.Routes["/api/v1/info"] = """{"mmu":true,"name":"Core One"}""";
        _printer.Routes["/api/v1/job"] = "";
        _printer.Routes["/api/v1/status"] = """{"printer":{"state":"IDLE"}}""";
    }

    public void Dispose() => _factory.Dispose();

    private async Task<PrinterResponse> CreatePrinterAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Core One", PrinterType.PrusaLink, "http://prusa.local", "abc123", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<Item> CreateItemAsync(string tag, string name = "Prusament PLA") =>
        (await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, tag, name, null, null, new FilamentData { Type = "PLA" }, 1000, SpoolsInStock: 2)))
            .Content.ReadFromJsonAsync<Item>())!;

    private async Task<ReaderResponse> ReportAsync(string printerId, params string?[] slots)
    {
        var response = await _client.PutAsJsonAsync($"/api/printers/{printerId}/reader", new ReaderReport(slots));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReaderResponse>())!;
    }

    private async Task<List<Spool>> SpoolsInAsync(string printerId) =>
        (await _client.GetFromJsonAsync<List<Spool>>($"/api/spools?printerId={printerId}"))!;

    [Fact]
    public async Task InsertedSwatchLoadsASpoolOfItsItemIntoTheSlot()
    {
        var printer = await CreatePrinterAsync();
        var white = await CreateItemAsync("04378F53CB2A81", "Weiß");

        // ESPHome schreibt die UID mit Bindestrichen
        var result = await ReportAsync(printer.Id, null, null, "04-37-8f-53-cb-2a-81", null, null);

        Assert.Equal(new[] { "empty", "empty", "known", "empty", "empty" }, result.Slots.Select(s => s.State).ToArray());
        Assert.Equal("Weiß #1", result.Slots[2].Label);
        var spool = Assert.Single(await SpoolsInAsync(printer.Id));
        Assert.Equal(white.Id, spool.ItemId);
        Assert.Equal(2, spool.Tool);

        var after = (await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!;
        Assert.NotNull(after.ReaderSeenAt);
    }

    [Fact]
    public async Task RepeatedReportDoesNotUndoAMoveInTheApp()
    {
        var printer = await CreatePrinterAsync();
        await CreateItemAsync("AA01");
        await ReportAsync(printer.Id, "AA01");
        var spool = Assert.Single(await SpoolsInAsync(printer.Id));

        // In der App ins Lager gelegt, das Plättchen steckt noch
        await _client.PutAsJsonAsync($"/api/spools/{spool.Id}", new SpoolUpdateRequest(spool.RemainingWeight, null, null));
        await ReportAsync(printer.Id, "AA01");
        Assert.Empty(await SpoolsInAsync(printer.Id));

        // Neu eingesteckt: wieder im Slot
        await ReportAsync(printer.Id, (string?)null);
        await ReportAsync(printer.Id, "AA01");
        Assert.Equal(spool.Id, Assert.Single(await SpoolsInAsync(printer.Id)).Id);
    }

    [Fact]
    public async Task UnknownSwatchIsLoadedOnceItIsAssignedInTheApp()
    {
        var printer = await CreatePrinterAsync();

        var first = await ReportAsync(printer.Id, "BB02");
        Assert.Equal("unknown", first.Slots[0].State);
        Assert.Empty(await SpoolsInAsync(printer.Id));

        await CreateItemAsync("BB02");
        var second = await ReportAsync(printer.Id, "BB02");
        Assert.Equal("known", second.Slots[0].State);
        Assert.Equal(0, Assert.Single(await SpoolsInAsync(printer.Id)).Tool);
    }

    [Fact]
    public async Task TapLoadsTheSpoolIntoTheChosenSlotEveryTime()
    {
        var printer = await CreatePrinterAsync();
        await CreateItemAsync("CC03");

        var response = await _client.PostAsJsonAsync($"/api/printers/{printer.Id}/reader/tap", new ReaderTap(3, "cc:03"));
        var slot = (await response.Content.ReadFromJsonAsync<ReaderSlot>())!;
        Assert.Equal("known", slot.State);
        Assert.Equal(3, Assert.Single(await SpoolsInAsync(printer.Id)).Tool);

        await _client.PostAsJsonAsync($"/api/printers/{printer.Id}/reader/tap", new ReaderTap(1, "CC03"));
        Assert.Equal(1, Assert.Single(await SpoolsInAsync(printer.Id)).Tool);

        var invalid = await _client.PostAsJsonAsync($"/api/printers/{printer.Id}/reader/tap", new ReaderTap(-1, ""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task LiveStatusShowsTheSwatchAndWarnsWhenTheAppSaysOtherwise()
    {
        var printer = await CreatePrinterAsync();
        await CreateItemAsync("DD04", "Rot");
        var black = await CreateItemAsync("EE05", "Schwarz");
        await ReportAsync(printer.Id, "DD04");

        // In der App steckt eine andere Sorte im Slot, das Plättchen blieb
        await _client.PostAsJsonAsync($"/api/items/{black.Id}/spools", new OpenSpoolRequest(null, printer.Id, 0));
        await ReportAsync(printer.Id, "DD04");

        var live = (await _client.GetFromJsonAsync<PrinterLiveResponse>($"/api/printers/{printer.Id}/status"))!;
        Assert.Equal("DD04", live.Tools[0].Reported!.TagUid);
        Assert.Contains(live.Hints, h => h.Kind == HintKind.TagMismatch && h.Tool == 0);
    }

    [Theory]
    [InlineData("04-37-8f-53", "04378F53")]
    [InlineData("04:37:8F:53", "04378F53")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void NormalizesUids(string? raw, string? expected) => Assert.Equal(expected, PrinterReader.NormalizeUid(raw));
}
