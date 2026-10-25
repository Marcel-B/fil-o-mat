using System.Net;
using System.Net.Http.Json;
using System.Text;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;

namespace Filomat.Api.Tests;

/// <summary>Nimmt Meldungen an ntfy entgegen.</summary>
public class FakeNtfy : HttpMessageHandler
{
    public record Message(string Url, string Title, string Body, string? Tags, string? Authorization);

    public List<Message> Messages { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var title = request.Headers.TryGetValues("Title", out var values) ? Decode(values.Single()) : "";
        var tags = request.Headers.TryGetValues("Tags", out var tagValues) ? tagValues.Single() : null;
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (Messages)
            Messages.Add(new Message(request.RequestUri!.ToString(), title, body, tags, request.Headers.Authorization?.ToString()));
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    /// <summary>RFC 2047, wie der Notifier den Titel schickt.</summary>
    private static string Decode(string header) =>
        header.StartsWith("=?UTF-8?B?", StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(header[10..^2]))
            : header;
}

/// <summary>Zustand der Drucker, Hinweise, Meldungen und Statistik.</summary>
public class LiveStatusTests : IDisposable
{
    private readonly FakeMoonraker _moonraker = new();
    private readonly FakeAnycubic _anycubic = new();
    private readonly FakeBambu _bambu = new();
    private readonly FakeNtfy _ntfy = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public LiveStatusTests()
    {
        _factory = new ApiFactory { PrinterHandler = _moonraker, AnycubicLan = _anycubic, BambuLan = _bambu, NtfyHandler = _ntfy };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private const string VoronPrinting = """
        {"result":{"status":{
          "print_stats":{"state":"printing","filename":"teile/benchy.gcode","print_duration":600,"message":"","info":{"current_layer":12,"total_layer":80}},
          "virtual_sdcard":{"progress":0.25},"display_status":{"progress":0.25},
          "toolhead":{"extruder":"extruder"},
          "extruder":{"temperature":214.6,"target":215},
          "heater_bed":{"temperature":59.8,"target":60}}}}
        """;

    private const string VoronMetadata = """
        {"result":{"filename":"teile/benchy.gcode","estimated_time":2400,"filament_weight_total":40,
          "thumbnails":[{"width":32,"height":32,"relative_path":".thumbs/benchy-32x32.png"},
                        {"width":300,"height":300,"relative_path":".thumbs/benchy-300x300.png"}]}}
        """;

    private async Task<PrinterResponse> CreatePrinterAsync(string name, PrinterType type, string url, string? apiKey = null)
    {
        var response = await _client.PostAsJsonAsync("/api/printers", new PrinterRequest(name, type, url, apiKey, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    private async Task<(Item Item, Spool Spool)> PlaceSpoolAsync(string tag, string printerId, int tool, string type = "PLA",
        double? remaining = null, string? color = null, string? minTemp = null, string? maxTemp = null)
    {
        var item = (await (await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, tag, $"{type} {tag}", null, null,
            new FilamentData { Type = type, ColorHex = color, MinTemp = minTemp, MaxTemp = maxTemp }, 1000))).Content.ReadFromJsonAsync<Item>())!;
        var spool = (await (await _client.PostAsJsonAsync($"/api/items/{item.Id}/spools",
            new OpenSpoolRequest(remaining, printerId, tool))).Content.ReadFromJsonAsync<Spool>())!;
        return (item, spool);
    }

    private async Task SyncAsync(string printerId) =>
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/printers/{printerId}/sync", null)).StatusCode);

    private async Task<PrinterLiveResponse> StatusAsync(string printerId) =>
        (await _client.GetFromJsonAsync<PrinterLiveResponse>($"/api/printers/{printerId}/status"))!;

    [Fact]
    public async Task MoonrakerReportsTheRunningPrintAndWarnsWhenTheSpoolIsTooShort()
    {
        _moonraker.Routes["/printer/objects/query"] = VoronPrinting;
        _moonraker.Routes["/server/files/metadata"] = VoronMetadata;
        _moonraker.Routes["/server/files/gcodes/teile/.thumbs/benchy-300x300.png"] = "PNG";
        var printer = await CreatePrinterAsync("Voron", PrinterType.Moonraker, "http://voron.local:7125");
        await PlaceSpoolAsync("V1", printer.Id, 0, remaining: 20, minTemp: "190", maxTemp: "220");
        await SyncAsync(printer.Id);

        var live = await StatusAsync(printer.Id);
        Assert.Equal(PrintState.Printing, live.Status.State);
        Assert.Equal("benchy", live.Status.JobName);
        Assert.Equal(25, live.Status.Progress);
        Assert.Equal(1800, live.Status.RemainingSeconds);
        Assert.Equal(12, live.Status.Layer);
        Assert.Equal(80, live.Status.TotalLayers);
        Assert.True(live.Status.HasThumbnail);
        Assert.Contains(live.Status.Heaters!, h => h.Name == "Düse" && h.Target == 215 && h.Tool == 0);
        Assert.Contains(live.Status.Heaters!, h => h.Name == "Bett" && h.Target == 60);

        // 40 g geplant, 75 % fehlen noch: 30 g, auf der Spule sind 20 g
        var tool = Assert.Single(live.Tools);
        Assert.Equal(30, tool.NeededGrams);
        Assert.False(tool.Enough);
        var hint = Assert.Single(live.Hints);
        Assert.Equal(HintKind.SpoolShort, hint.Kind);
        Assert.Contains("noch 20 g", hint.Text);

        // Die Meldung kommt nur einmal pro Druck
        await SyncAsync(printer.Id);
        var message = Assert.Single(_ntfy.Messages);
        Assert.Equal("Voron: Spule reicht nicht", message.Title);
        Assert.StartsWith("benchy: ", message.Body);

        var thumbnail = await _client.GetAsync($"/api/printers/{printer.Id}/thumbnail");
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
        Assert.Equal("PNG", await thumbnail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WarnsWhenTheNozzleHeatsOutsideTheRangeOfTheLoadedFilament()
    {
        _moonraker.Routes["/printer/objects/query"] = VoronPrinting;
        _moonraker.Routes["/server/files/metadata"] = VoronMetadata;
        var printer = await CreatePrinterAsync("Voron", PrinterType.Moonraker, "http://voron.local:7125");
        await PlaceSpoolAsync("V1", printer.Id, 0, type: "PETG", minTemp: "230", maxTemp: "250");
        await SyncAsync(printer.Id);

        var hint = Assert.Single((await StatusAsync(printer.Id)).Hints);
        Assert.Equal(HintKind.Temperature, hint.Kind);
        Assert.Contains("215 °C", hint.Text);
        Assert.Contains("230–250 °C", hint.Text);
    }

    [Fact]
    public async Task IgnoresTheProbingTemperatureAtTheStartOfAPrusaPrint()
    {
        _moonraker.Routes["/api/v1/info"] = """{"mmu":false}""";
        _moonraker.Routes["/api/v1/status"] = """
            {"job":{"id":18,"progress":0,"time_remaining":3000,"time_printing":30},
             "printer":{"state":"PRINTING","temp_nozzle":150.2,"target_nozzle":170,"temp_bed":58.1,"target_bed":60}}
            """;
        _moonraker.Routes["/api/v1/job"] = """
            {"id":18,"state":"PRINTING","progress":0,"time_printing":30,
             "file":{"name":"BOX~1.BGC","display_name":"box.bgcode","path":"/usb","m_timestamp":1760000000,
                     "meta":{"filament used [g]":50,"filament_type":"PLA"}}}
            """;
        var printer = await CreatePrinterAsync("MK4S", PrinterType.PrusaLink, "http://prusa.local", "abc123");
        await PlaceSpoolAsync("P1", printer.Id, 0, remaining: 500, minTemp: "190", maxTemp: "230");
        await SyncAsync(printer.Id);

        Assert.Empty((await StatusAsync(printer.Id)).Hints);
        Assert.Empty(_ntfy.Messages);
    }

    [Fact]
    public async Task NotifiesFinishedPrintsWithTheRemainingWeight()
    {
        var printer = await CreatePrinterAsync("Voron", PrinterType.Moonraker, "http://voron.local:7125");
        var (item, _) = await PlaceSpoolAsync("V1", printer.Id, 0);
        _moonraker.Body = """
            {"result":{"count":1,"jobs":[{"job_id":"000001","filename":"benchy.gcode","status":"completed",
              "end_time":4102444800,"filament_used":5000,"print_duration":4980,
              "metadata":{"filament_total":10000,"filament_weight_total":30,"filament_type":"PLA"}}]}}
            """;
        await SyncAsync(printer.Id);

        var message = Assert.Single(_ntfy.Messages);
        Assert.Equal("Voron: benchy.gcode fertig", message.Title);
        Assert.Equal($"{item.Name} #1 −15 g, noch 985 g\nDruckzeit 1:23 h", message.Body);
        Assert.Equal("white_check_mark", message.Tags);

        var entry = Assert.Single((await _client.GetFromJsonAsync<List<Consumption>>($"/api/consumption?printerId={printer.Id}"))!);
        Assert.Equal(4980, entry.DurationSeconds);
    }

    [Fact]
    public async Task U1ReportsTheTagInEachHead()
    {
        _moonraker.Routes["/printer/objects/query"] = """
            {"result":{"status":{
              "print_stats":{"state":"standby","filename":""},
              "toolhead":{"extruder":"extruder1"},
              "extruder":{"temperature":25,"target":0},"extruder1":{"temperature":26,"target":0},
              "extruder2":{"temperature":25,"target":0},"extruder3":{"temperature":25,"target":0},
              "heater_bed":{"temperature":24,"target":0},
              "filament_detect":{"info":[
                {"VENDOR":"Sunlu","MAIN_TYPE":"PLA","SUB_TYPE":"","RGB_1":16777215,"CARD_UID":[4,161,59,82,203,42,129]},
                {"VENDOR":"NONE","MAIN_TYPE":"NONE","SUB_TYPE":"NONE","RGB_1":0,"CARD_UID":[0,0,0,0,0,0,0]},
                {"VENDOR":"NONE","MAIN_TYPE":"NONE","SUB_TYPE":"NONE","RGB_1":0,"CARD_UID":[0,0,0,0,0,0,0]},
                {"VENDOR":"NONE","MAIN_TYPE":"NONE","SUB_TYPE":"NONE","RGB_1":0,"CARD_UID":[0,0,0,0,0,0,0]}]},
              "print_task_config":{"extruder_map_table":[0,1,2,3]}}}}
            """;
        var printer = await CreatePrinterAsync("U1", PrinterType.SnapmakerU1, "http://192.168.2.155");

        var live = await StatusAsync(printer.Id);
        Assert.Equal(PrintState.Idle, live.Status.State);
        Assert.Equal(1, live.Status.ActiveTool);
        Assert.Equal(["Düse 1", "Düse 2", "Düse 3", "Düse 4", "Bett"], live.Status.Heaters!.Select(h => h.Name));
        Assert.Equal(4, live.Tools.Count);
        // Das erkannte Tag wurde als Spule übernommen
        Assert.Equal("04A13B52CB2A81", live.Tools[0].Reported!.TagUid);
        Assert.Equal("Sunlu PLA", live.Tools[0].ItemName);
        Assert.Equal("FFFFFF", live.Tools[0].ColorHex);
        Assert.Null(live.Tools[1].Reported!.Loaded);
        Assert.Empty(live.Hints);
    }

    [Fact]
    public async Task AnycubicComparesTheSlotsWithTheAppAndWarnsAboutHumidity()
    {
        _anycubic.Boxes = """
            [{"id":0,"status":1,"loaded_slot":0,"temp":31,"humidity":52,
              "slots":[{"index":0,"type":"PLA","color":[0,0,0],"status":5,"edit_status":0},
                       {"index":1,"type":"PETG","color":[255,0,0],"status":4,"edit_status":0},
                       {"index":2,"type":"","color":[0,0,0],"status":0,"edit_status":0},
                       {"index":3,"type":"TPU","color":[0,0,255],"status":4,"edit_status":0}]}]
            """;
        _anycubic.Info = FakeAnycubic.Printing(FakeAnycubic.Project(614707220, 1000, 50, 0));
        var printer = await CreatePrinterAsync("Kobra X", PrinterType.Anycubic, "http://192.168.2.160");
        await PlaceSpoolAsync("K0", printer.Id, 0, color: "000000", remaining: 1);
        await PlaceSpoolAsync("K1", printer.Id, 1, type: "PLA");
        await PlaceSpoolAsync("K2", printer.Id, 2);
        await SyncAsync(printer.Id);

        var live = await StatusAsync(printer.Id);
        Assert.Equal(PrintState.Printing, live.Status.State);
        Assert.Equal(42 * 60, live.Status.RemainingSeconds);
        Assert.Equal(3, live.Status.Layer);
        Assert.True(live.Status.JobGramsEstimated);
        // 1 m gefördert bei 50 %: noch etwa 1 m PLA aus Slot 1
        Assert.Equal(Math.Round(FilamentMath.GramsFromLength(1000, "PLA"), 1), live.Tools[0].NeededGrams);

        Assert.Contains(live.Hints, h => h is { Kind: HintKind.SpoolShort, Tool: 0 } && h.Text.Contains("geschätzt"));
        Assert.Contains(live.Hints, h => h is { Kind: HintKind.SlotMismatch, Tool: 1, Warning: true } && h.Text.Contains("PETG"));
        Assert.Contains(live.Hints, h => h is { Kind: HintKind.SlotEmpty, Tool: 2 });
        Assert.Contains(live.Hints, h => h is { Kind: HintKind.SlotUnknown, Tool: 3 } && h.Text.Contains("TPU"));
        var humidity = Assert.Single(live.Hints, h => h.Kind == HintKind.Humidity);
        Assert.Equal("ACE 1: 52 % Feuchte, PETG, TPU sollte trocknen.", humidity.Text);
        Assert.Equal(52, Assert.Single(live.Status.Dryers!).HumidityPercent);
    }

    [Fact]
    public async Task BambuReportsAmsSlotsAndTheHumidityLevel()
    {
        _bambu.Print = FakeBambu.Report("RUNNING", 50, trayNow: 2, ams: FakeBambu.Ams(2).Replace("\"humidity\":\"4\"", "\"humidity\":\"1\""));
        var printer = await CreatePrinterAsync("P1S", PrinterType.Bambu, "http://192.168.2.170", "12345678");
        await SyncAsync(printer.Id);

        var live = await StatusAsync(printer.Id);
        Assert.Equal(PrintState.Printing, live.Status.State);
        Assert.Equal(2, live.Status.ActiveTool);
        Assert.Equal(12 * 60, live.Status.RemainingSeconds);
        Assert.Equal(50, live.Status.TotalLayers);
        Assert.Equal(5, live.Tools.Count);
        Assert.Equal(80, live.Tools[0].Reported!.RemainPercent);
        Assert.False(live.Tools[3].Reported!.Loaded);
        Assert.Equal("PETG", live.Tools[4].Reported!.Type);

        var dryer = Assert.Single(live.Status.Dryers!);
        Assert.Equal(1, dryer.HumidityLevel);
        var hint = Assert.Single(live.Hints, h => h.Kind == HintKind.Humidity);
        Assert.Contains("Feuchtestufe 1 von 5, PETG sollte trocknen", hint.Text);
    }

    [Fact]
    public async Task PrusaLinkReportsTemperaturesAndTheSlicedWeight()
    {
        _moonraker.Routes["/api/v1/info"] = """{"mmu":false}""";
        _moonraker.Routes["/api/v1/status"] = """
            {"job":{"id":17,"progress":40,"time_remaining":3000,"time_printing":1200},
             "printer":{"state":"PRINTING","temp_nozzle":219.8,"target_nozzle":220,"temp_bed":60.1,"target_bed":60}}
            """;
        _moonraker.Routes["/api/v1/job"] = """
            {"id":17,"state":"PRINTING","progress":40,"time_printing":1200,
             "file":{"name":"BOX~1.BGC","display_name":"box.bgcode","path":"/usb","m_timestamp":1760000000,
                     "refs":{"thumbnail":"/thumb/l/usb/BOX~1.BGC"},
                     "meta":{"filament used [g]":50,"filament used [mm]":16000,"filament_type":"PLA"}}}
            """;
        _moonraker.Routes["/thumb/l/usb/BOX~1.BGC"] = "PNG";
        var printer = await CreatePrinterAsync("MK4", PrinterType.PrusaLink, "http://prusa.local", "abc123");
        await PlaceSpoolAsync("P1", printer.Id, 0, remaining: 500);
        await SyncAsync(printer.Id);

        var live = await StatusAsync(printer.Id);
        Assert.Equal(PrintState.Printing, live.Status.State);
        Assert.Equal("box.bgcode", live.Status.JobName);
        Assert.Equal(3000, live.Status.RemainingSeconds);
        Assert.Equal(220, live.Status.Heaters!.Single(h => h.Name == "Düse").Target);
        Assert.Equal(30, live.Tools[0].NeededGrams);
        Assert.True(live.Tools[0].Enough);
        Assert.Empty(live.Hints);
        Assert.Equal("PNG", await _client.GetStringAsync($"/api/printers/{printer.Id}/thumbnail"));
    }

    [Fact]
    public async Task UnreachablePrinterIsOffline()
    {
        _moonraker.Status = HttpStatusCode.ServiceUnavailable;
        var printer = await CreatePrinterAsync("Voron", PrinterType.Moonraker, "http://voron.local:7125");

        var all = (await _client.GetFromJsonAsync<List<PrinterLiveResponse>>("/api/printers/status"))!;
        var live = Assert.Single(all);
        Assert.Equal(printer.Id, live.PrinterId);
        Assert.Equal(PrintState.Offline, live.Status.State);
        Assert.NotNull(live.Status.Message);
    }

    [Fact]
    public async Task StatsCountJobsGramsAndPrintTime()
    {
        var printer = await CreatePrinterAsync("Voron", PrinterType.Moonraker, "http://voron.local:7125");
        var (item, _) = await PlaceSpoolAsync("V1", printer.Id, 0);
        _moonraker.Body = """
            {"result":{"count":2,"jobs":[
              {"job_id":"000001","filename":"a.gcode","status":"completed","end_time":4102444800,"filament_used":5000,"print_duration":3600,
               "metadata":{"filament_total":10000,"filament_weight_total":30}},
              {"job_id":"000002","filename":"b.gcode","status":"cancelled","end_time":4102444900,"filament_used":1000,"print_duration":600,
               "metadata":{"filament_total":10000,"filament_weight_total":30}}]}}
            """;
        await SyncAsync(printer.Id);
        var all = (await _client.GetFromJsonAsync<StatsResponse>("/api/stats?months=120"))!;
        Assert.Equal(2, all.Totals.Jobs);
        Assert.Equal(1, all.Totals.Completed);
        Assert.Equal(1, all.Totals.Cancelled);
        Assert.Equal(18, all.Totals.Grams);
        Assert.Equal(3, all.Totals.CancelledGrams);
        Assert.Equal(4200, all.Totals.PrintSeconds);
        Assert.Equal("Voron", Assert.Single(all.Printers).Name);
        Assert.Equal(item.Id, Assert.Single(all.Items).ItemId);
        Assert.Equal("PLA", Assert.Single(all.Types).Type);
        Assert.Equal(120, all.Months.Count);
    }

    [Fact]
    public async Task NotificationSettingsCanBeChangedFromTheApp()
    {
        var initial = (await _client.GetFromJsonAsync<NotificationSettingsResponse>("/api/settings/notifications"))!;
        Assert.Equal("https://ntfy.test/filomat", initial.Url);
        Assert.False(initial.FromApp);

        var invalid = await _client.PutAsJsonAsync("/api/settings/notifications", new NotificationSettingsRequest("ntfy.sh/x", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var saved = await (await _client.PutAsJsonAsync("/api/settings/notifications",
            new NotificationSettingsRequest("https://ntfy.example.de/drucker", "tk_geheim"))).Content.ReadFromJsonAsync<NotificationSettingsResponse>();
        Assert.True(saved!.FromApp);
        Assert.True(saved.HasToken);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/settings/notifications/test", null)).StatusCode);
        var message = Assert.Single(_ntfy.Messages);
        Assert.Equal("https://ntfy.example.de/drucker", message.Url);
        Assert.Equal("Bearer tk_geheim", message.Authorization);
        Assert.Equal("fil-O-mat", message.Title);

        // Leer: zurück zur Server-Konfiguration
        var reset = await (await _client.PutAsJsonAsync("/api/settings/notifications",
            new NotificationSettingsRequest("", null))).Content.ReadFromJsonAsync<NotificationSettingsResponse>();
        Assert.False(reset!.FromApp);
        Assert.False(reset.HasToken);
    }
}
