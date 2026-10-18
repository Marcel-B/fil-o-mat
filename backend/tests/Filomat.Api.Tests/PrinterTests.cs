using System.Net;
using System.Net.Http.Json;
using System.Text;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;

namespace Filomat.Api.Tests;

/// <summary>Liefert eine feste Moonraker-Antwort und merkt sich die Anfragen.</summary>
public class FakeMoonraker : HttpMessageHandler
{
    public string Body { get; set; } = """{"result":{"count":0,"jobs":[]}}""";
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Antworten pro Pfad; andere Pfade bekommen <see cref="Body"/>.</summary>
    public Dictionary<string, string> Routes { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = new StringContent(Routes.GetValueOrDefault(request.RequestUri!.AbsolutePath, Body), Encoding.UTF8, "application/json"),
        });
    }

    public static string History(params (string id, string status, double? endTime, double used, double? total, double? weight)[] jobs)
    {
        var items = jobs.Select(j => new Dictionary<string, object?>
        {
            ["job_id"] = j.id,
            ["filename"] = $"{j.id}.gcode",
            ["status"] = j.status,
            ["end_time"] = j.endTime,
            ["filament_used"] = j.used,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["filament_total"] = j.total,
                ["filament_weight_total"] = j.weight,
                ["filament_type"] = "PLA",
            },
        }).ToList();
        return System.Text.Json.JsonSerializer.Serialize(new { result = new { count = items.Count, jobs = items } });
    }
}

public class PrinterTests : IDisposable
{
    private readonly FakeMoonraker _moonraker = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PrinterTests()
    {
        _factory = new ApiFactory { PrinterHandler = _moonraker };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private static double InOneHour => DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

    private async Task<PrinterResponse> CreatePrinterAsync(string? apiKey = null)
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Voron Zero", PrinterType.Moonraker, "http://voron.local:7125", apiKey, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<Item> CreateSpoolAsync(string tag, string containerId, double? net = 1000, double? remaining = null)
    {
        var response = await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, tag, "PLA Schwarz", null, containerId,
            new FilamentData { Type = "PLA" }, net, remaining));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    [Fact]
    public async Task CreatingPrinterCreatesItsContainer()
    {
        var printer = await CreatePrinterAsync("geheim");
        Assert.True(printer.HasApiKey);

        var container = await _client.GetFromJsonAsync<Container>($"/api/containers/{printer.ContainerId}");
        Assert.Equal("Voron Zero", container!.Name);
        Assert.Equal($"PRINTER_{printer.Id}", container.RfidTag);

        var json = await _client.GetStringAsync($"/api/printers/{printer.Id}");
        Assert.DoesNotContain("geheim", json);
        Assert.Contains("\"Moonraker\"", json);
    }

    [Fact]
    public async Task RejectsInvalidAddress()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("X", PrinterType.Moonraker, "voron.local", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SyncDeductsFromSingleSpoolAndIsIdempotent()
    {
        var printer = await CreatePrinterAsync("geheim");
        var spool = await CreateSpoolAsync("SPOOL1", printer.ContainerId);
        // Slicer: 10 m = 30 g, gedruckt 5 m => 15 g
        _moonraker.Body = FakeMoonraker.History(
            ("000001", "completed", InOneHour, 5000, 10000, 30),
            ("000002", "in_progress", null, 100, 10000, 30));

        var first = await (await _client.PostAsync($"/api/printers/{printer.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(1, first!.Recorded);
        Assert.Null(first.Error);
        Assert.Equal("geheim", _moonraker.Requests[0].Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("/server/history/list", _moonraker.Requests[0].RequestUri!.ToString());

        var second = await (await _client.PostAsync($"/api/printers/{printer.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(0, second!.Recorded);

        var item = await _client.GetFromJsonAsync<Item>($"/api/items/{spool.Id}");
        Assert.Equal(985, item!.RemainingWeight);

        var entries = await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?itemId={spool.Id}");
        Assert.Equal(15, Assert.Single(entries!).Grams);
    }

    [Fact]
    public async Task IgnoresJobsBeforePrinterWasAdded()
    {
        var printer = await CreatePrinterAsync();
        await CreateSpoolAsync("SPOOL1", printer.ContainerId);
        _moonraker.Body = FakeMoonraker.History(
            ("old", "completed", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(), 5000, null, null));

        var result = await (await _client.PostAsync($"/api/printers/{printer.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(0, result!.Recorded);
    }

    [Fact]
    public async Task UnclearSpoolIsRecordedUnassignedAndCanBeAssigned()
    {
        var printer = await CreatePrinterAsync();
        var a = await CreateSpoolAsync("SPOOL1", printer.ContainerId, remaining: 500);
        await CreateSpoolAsync("SPOOL2", printer.ContainerId);
        _moonraker.Body = FakeMoonraker.History(("000001", "completed", InOneHour, 5000, 10000, 30));

        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        var open = await _client.GetFromJsonAsync<List<Consumption>>("/api/consumption?unassigned=true");
        var entry = Assert.Single(open!);

        var assign = await _client.PostAsJsonAsync($"/api/consumption/{entry.Id}/assign", new AssignConsumptionRequest(a.Id));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(485, (await _client.GetFromJsonAsync<Item>($"/api/items/{a.Id}"))!.RemainingWeight);

        var again = await _client.PostAsJsonAsync($"/api/consumption/{entry.Id}/assign", new AssignConsumptionRequest(a.Id));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task UnreachablePrinterStoresError()
    {
        var printer = await CreatePrinterAsync();
        _moonraker.Status = HttpStatusCode.Unauthorized;

        var result = await (await _client.PostAsync($"/api/printers/{printer.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result!.Error);
        Assert.NotNull((await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!.LastSyncError);
    }

    [Fact]
    public async Task PrinterContainerCannotBeDeletedButPrinterCan()
    {
        var printer = await CreatePrinterAsync();
        var spool = await CreateSpoolAsync("SPOOL1", printer.ContainerId);

        var deleteContainer = await _client.DeleteAsync($"/api/containers/{printer.ContainerId}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteContainer.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/printers/{printer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/containers/{printer.ContainerId}")).StatusCode);
        Assert.Equal(Container.LooseId, (await _client.GetFromJsonAsync<Item>($"/api/items/{spool.Id}"))!.ContainerId);
    }
}

public class FilamentMathTests
{
    [Fact]
    public void UsesSlicerRatioWhenAvailable() =>
        Assert.Equal(15, FilamentMath.GramsForJob(new PrintJob("1", "x", "completed", DateTime.UtcNow, 5000, 10000, 30), "PETG"), 6);

    [Fact]
    public void FallsBackToDensity()
    {
        // 1 m PLA 1,75 mm ≈ 2,98 g
        Assert.Equal(2.98, FilamentMath.GramsForJob(new PrintJob("1", "x", "completed", DateTime.UtcNow, 1000), "PLA"), 2);
        Assert.True(FilamentMath.GramsFromLength(1000, "ABS") < FilamentMath.GramsFromLength(1000, "PLA"));
    }

    [Fact]
    public void DeductNeverGoesBelowZero()
    {
        var item = new Item { Id = "1", RfidTag = "t", Name = "n", NetWeight = 10 };
        ConsumptionSync.Deduct(item, 25);
        Assert.Equal(0, item.RemainingWeight);
    }
}

public class SpoolStockTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public SpoolStockTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void RunningOutOpensSpoolFromStock()
    {
        var item = new Item { Id = "1", RfidTag = "t", Name = "n", NetWeight = 1000, RemainingWeight = 30, SpoolsInStock = 2 };
        ConsumptionSync.Deduct(item, 50);
        Assert.Equal(980, item.RemainingWeight);
        Assert.Equal(1, item.SpoolsInStock);
    }

    [Fact]
    public void WithoutStockRemainingStopsAtZero()
    {
        var item = new Item { Id = "1", RfidTag = "t", Name = "n", NetWeight = 1000, RemainingWeight = 30, SpoolsInStock = 0 };
        ConsumptionSync.Deduct(item, 50);
        Assert.Equal(0, item.RemainingWeight);
        Assert.Equal(0, item.SpoolsInStock);
    }

    [Fact]
    public async Task OpenSpoolEndpointRefillsFromStock()
    {
        var created = await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, "T1", "PLA", null, null, null, 1000, 12, null, 3))).Content.ReadFromJsonAsync<Item>();

        var opened = await (await _client.PostAsync($"/api/items/{created!.Id}/open-spool", null)).Content.ReadFromJsonAsync<Item>();
        Assert.Equal(1000, opened!.RemainingWeight);
        Assert.Equal(2, opened.SpoolsInStock);

        var noWeight = await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, "T2", "PLA", null, null, null))).Content.ReadFromJsonAsync<Item>();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/items/{noWeight!.Id}/open-spool", null)).StatusCode);
    }
}
