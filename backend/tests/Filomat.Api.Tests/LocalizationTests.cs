using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Localization;
using Filomat.Api.Printers;

namespace Filomat.Api.Tests;

public class LocalizationTests : IDisposable
{
    private readonly FakeMoonraker _moonraker = new();
    private readonly FakeNtfy _ntfy = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public LocalizationTests()
    {
        _factory = new ApiFactory { PrinterHandler = _moonraker, NtfyHandler = _ntfy };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private static HttpRequestMessage English(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.AcceptLanguage.ParseAdd("en-GB");
        return request;
    }

    [Fact]
    public async Task ErrorsFollowAcceptLanguage()
    {
        var german = await _client.PostAsJsonAsync("/api/containers", new ContainerRequest(null, "TAG1", " ", null, null));
        Assert.Contains("Name fehlt.", await german.Content.ReadAsStringAsync());

        var english = await _client.SendAsync(English(HttpMethod.Post, "/api/containers", new ContainerRequest(null, "TAG1", " ", null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, english.StatusCode);
        Assert.Contains("Name is missing.", await english.Content.ReadAsStringAsync());

        // Unbekannte Sprachen bekommen Deutsch
        var french = new HttpRequestMessage(HttpMethod.Post, "/api/containers") { Content = JsonContent.Create(new ContainerRequest(null, "TAG1", " ", null, null)) };
        french.Headers.AcceptLanguage.ParseAdd("fr-FR");
        Assert.Contains("Name fehlt.", await (await _client.SendAsync(french)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PrinterStatusAndHintsInEnglish()
    {
        _moonraker.Routes["/printer/objects/query"] = """
            {"result":{"status":{
              "print_stats":{"state":"printing","filename":"benchy.gcode","print_duration":600,"message":""},
              "virtual_sdcard":{"progress":0.25},"toolhead":{"extruder":"extruder"},
              "extruder":{"temperature":214.6,"target":215},"heater_bed":{"temperature":59.8,"target":60}}}}
            """;
        _moonraker.Routes["/server/files/metadata"] = """{"result":{"filename":"benchy.gcode","filament_weight_total":40}}""";
        var printer = (await (await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Voron", PrinterType.Moonraker, "http://voron.local:7125", null, null))).Content.ReadFromJsonAsync<PrinterResponse>())!;
        var item = (await (await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, "V1", "PLA V1", null, null,
            new FilamentData { Type = "PLA" }, 1000))).Content.ReadFromJsonAsync<Item>())!;
        await _client.PostAsJsonAsync($"/api/items/{item.Id}/spools", new OpenSpoolRequest(20, printer.Id, 0));

        var response = await _client.SendAsync(English(HttpMethod.Get, $"/api/printers/{printer.Id}/status"));
        var live = (await response.Content.ReadFromJsonAsync<PrinterLiveResponse>())!;
        Assert.Contains(live.Status.Heaters!, h => h.Name == "Nozzle");
        Assert.Contains(live.Status.Heaters!, h => h.Name == "Bed");
        Assert.Equal("PLA V1 #1 has 20 g left, the print still needs 30 g.", Assert.Single(live.Hints).Text);
    }

    [Fact]
    public async Task NotificationsUseTheLanguageOfTheAppThatSetThemUp()
    {
        var saved = await _client.SendAsync(English(HttpMethod.Put, "/api/settings/notifications",
            new NotificationSettingsRequest("https://ntfy.test/filomat", null)));
        Assert.Equal("en", (await saved.Content.ReadFromJsonAsync<NotificationSettingsResponse>())!.Language);

        var printer = (await (await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Voron", PrinterType.Moonraker, "http://voron.local:7125", null, null))).Content.ReadFromJsonAsync<PrinterResponse>())!;
        var item = (await (await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, "V1", "PLA V1", null, null,
            new FilamentData { Type = "PLA" }, 1000))).Content.ReadFromJsonAsync<Item>())!;
        await _client.PostAsJsonAsync($"/api/items/{item.Id}/spools", new OpenSpoolRequest(null, printer.Id, 0));
        _moonraker.Body = """
            {"result":{"count":1,"jobs":[{"job_id":"000001","filename":"benchy.gcode","status":"completed",
              "end_time":4102444800,"filament_used":5000,"print_duration":4980,
              "metadata":{"filament_total":10000,"filament_weight_total":30,"filament_type":"PLA"}}]}}
            """;
        // Der Abgleich selbst kommt hier ohne Sprache an, die Meldung bleibt trotzdem englisch
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);

        var message = Assert.Single(_ntfy.Messages);
        Assert.Equal("Voron: benchy.gcode finished", message.Title);
        Assert.Equal("PLA V1 #1 −15 g, 985 g left\nPrint time 1:23 h", message.Body);
    }

    [Fact]
    public void RecognisesStoredMessagesWithValues()
    {
        using var language = L.Use("en");
        Assert.Equal("MQTT connection to the printer failed: Timeout.", L.T("MQTT-Verbindung zum Drucker fehlgeschlagen: Timeout."));
        Assert.Equal("Nozzle 2", L.T("Düse 2"));
        Assert.Equal("GitHub not reachable, the last check at 31.10. 08:15 failed.",
            L.T("GitHub nicht erreichbar, letzte Prüfung 31.10. 08:15 ging nicht."));
        // Meldungen des Druckers selbst bleiben, wie sie sind
        Assert.Equal("Filament runout", L.T("Filament runout"));
    }

    [Fact]
    public void GermanStaysWithoutLanguage()
    {
        using var language = L.Use(null);
        Assert.Equal("Düse 2", L.T("Düse 2"));
        Assert.Equal("Kopf 3", L.F("Kopf {0}", 3));
    }

    private static readonly Regex Placeholder = new(@"\{\d+(:[^}]*)?\}");

    [Fact]
    public void TranslationsKeepThePlaceholders()
    {
        foreach (var (german, english) in Texts())
            Assert.True(Placeholder.Matches(german).Select(m => m.Value).Order().SequenceEqual(Placeholder.Matches(english).Select(m => m.Value).Order()),
                $"Platzhalter passen nicht: „{german}“ → „{english}“");
    }

    /// <summary>Jeder Text, der im Backend durch L.T oder L.F läuft, braucht eine englische Fassung.</summary>
    [Fact]
    public void EveryTextHasAnEnglishVersion()
    {
        var source = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src", "Filomat.Api"));
        var used = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"L\.[TF]\(""((?:[^""\\]|\\.)*)""").Select(m => Regex.Unescape(m.Groups[1].Value)))
            .Distinct().ToList();
        Assert.NotEmpty(used);
        var known = Texts().Select(e => e.Key).ToHashSet();
        var missing = used.Where(text => !known.Contains(text)).ToList();
        Assert.True(missing.Count == 0, "Ohne englische Fassung: " + string.Join(" | ", missing));
    }

    private static IEnumerable<KeyValuePair<string, string>> Texts() => Localization.English.Texts.Concat(Localization.English.Messages);

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
