using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet.Exceptions;

namespace Filomat.Api.Tests;

/// <summary>Merkt sich Logzeilen mit Stufe.</summary>
public class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>Uhr, die nur weiterläuft, wenn der Test es sagt.</summary>
public class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Ausgeschaltete Drucker sind ein Zustand, kein Fehler: Logstufen, Backoff und Meldung, wenn einer mitten im Druck verschwindet.</summary>
public class ReachabilityTests : IDisposable
{
    private static readonly Exception Refused = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (voron-test.local:7125)",
        new SocketException((int)SocketError.ConnectionRefused));

    private readonly FakeMoonraker _moonraker = new();
    private readonly FakeNtfy _ntfy = new();
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ReachabilityTests()
    {
        _factory = new ApiFactory { PrinterHandler = _moonraker, NtfyHandler = _ntfy };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    public static TheoryData<Exception, bool> Errors() => new()
    {
        { Refused, true },
        { new HttpRequestException(HttpRequestError.NameResolutionError, "No such host"), true },
        { new TaskCanceledException("HttpClient.Timeout", new TimeoutException()), true },
        { new PrinterUnavailableException("MQTT-Verbindung zum Drucker fehlgeschlagen", new MqttCommunicationException(new SocketException((int)SocketError.HostUnreachable))), true },
        { new PrinterUnavailableException("MQTT-Verbindung", new MqttCommunicationTimedOutException()), true },
        { new HttpRequestException("PrusaLink lehnt den API-Schlüssel ab", null, HttpStatusCode.Unauthorized), false },
        { new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable), false },
        { new JsonException("unlesbar"), false },
        { new PrinterUnavailableException("Der LAN-Modus ist am Drucker aus."), false },
        { new PrinterUnavailableException("Der Drucker antwortet nicht auf MQTT-Abfragen (Seriennummer falsch?)."), false },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void TellsSwitchedOffFromFailing(Exception error, bool unreachable) =>
        Assert.Equal(unreachable, PrinterReachability.IsUnreachable(error));

    [Fact]
    public void SwitchedOffPrinterLogsOnlyTheChangesAsInfo()
    {
        var logger = new ListLogger<PrinterReachability>();
        var time = new ManualTime();
        var reachability = new PrinterReachability(logger, time);
        var printer = new Printer { Id = "p1", Name = "Barret", BaseUrl = "http://printer.local", ContainerId = "c1" };

        reachability.Succeeded(printer);
        for (var i = 0; i < 5; i++) reachability.Failed(printer, Refused);
        time.Now += TimeSpan.FromHours(3);
        reachability.Succeeded(printer);

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        var info = logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Equal(2, info.Count);
        Assert.StartsWith("Drucker Barret nicht erreichbar, vermutlich ausgeschaltet", info[0]);
        Assert.Equal("Drucker Barret wieder erreichbar, war 3 h nicht erreichbar", info[1]);
        Assert.Equal(4, logger.Entries.Count(e => e.Level == LogLevel.Debug));
    }

    [Fact]
    public void RealErrorsWarnOncePerMessage()
    {
        var logger = new ListLogger<PrinterReachability>();
        var reachability = new PrinterReachability(logger, new ManualTime());
        var printer = new Printer { Id = "p1", Name = "MK4S", BaseUrl = "http://printer.local", ContainerId = "c1" };
        var rejected = new HttpRequestException("PrusaLink lehnt den API-Schlüssel ab", null, HttpStatusCode.Unauthorized);

        for (var i = 0; i < 3; i++) reachability.Failed(printer, rejected);
        reachability.Failed(printer, new JsonException("unlesbar"));

        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Null(reachability.OfflineSince(printer.Id));
        Assert.False(reachability.ShouldWait(printer.Id));
    }

    [Fact]
    public void BacksOffAfterThreeMisses()
    {
        var time = new ManualTime();
        var reachability = new PrinterReachability(new ListLogger<PrinterReachability>(), time);
        var printer = new Printer { Id = "p1", Name = "Voron", BaseUrl = "http://printer.local", ContainerId = "c1" };

        for (var i = 0; i < PrinterReachability.FailuresBeforeBackoff - 1; i++) reachability.Failed(printer, Refused);
        Assert.False(reachability.ShouldWait(printer.Id));
        reachability.Failed(printer, Refused);
        Assert.True(reachability.ShouldWait(printer.Id));
        Assert.Equal(time.Now.UtcDateTime, reachability.OfflineSince(printer.Id));

        time.Now += PrinterReachability.BackoffInterval;
        Assert.False(reachability.ShouldWait(printer.Id));
    }

    [Fact]
    public async Task SwitchedOffPrinterIsMarkedOfflineAndPolledLess()
    {
        var printer = await CreatePrinterAsync();
        _moonraker.Throw = Refused;
        // Von Hand: wird immer gefragt
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/printers/{printer.Id}/sync", null)).StatusCode);

        var stored = (await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!;
        Assert.NotNull(stored.OfflineSince);
        Assert.NotNull(stored.LastSyncError);

        // Im Hintergrund: nach drei Versuchen ins Leere Pause
        for (var i = 0; i < 4; i++) await BackgroundSyncAsync(printer.Id);
        var asked = _moonraker.Requests.Count;
        await BackgroundSyncAsync(printer.Id);
        Assert.Equal(asked, _moonraker.Requests.Count);

        // Wieder an: Der Abgleich von Hand findet ihn sofort
        _moonraker.Throw = null;
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        stored = (await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{printer.Id}"))!;
        Assert.Null(stored.OfflineSince);
        Assert.Null(stored.LastSyncError);
        Assert.Empty(_ntfy.Messages);
    }

    [Fact]
    public async Task PrinterVanishingMidPrintIsReportedOnce()
    {
        var printer = await CreatePrinterAsync();
        _moonraker.Routes["/printer/objects/query"] = Printing(0.4);
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);

        _moonraker.Throw = Refused;
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        await _client.GetAsync($"/api/printers/{printer.Id}/status");

        var message = Assert.Single(_ntfy.Messages);
        Assert.Equal("Voron: nicht mehr erreichbar", message.Title);
        Assert.Contains("mitten im Druck wuerfel", message.Body);
    }

    [Fact]
    public async Task SwitchingOffRightAtTheEndIsNoNews()
    {
        var printer = await CreatePrinterAsync();
        _moonraker.Routes["/printer/objects/query"] = Printing(0.98);
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);

        _moonraker.Throw = Refused;
        await _client.PostAsync($"/api/printers/{printer.Id}/sync", null);
        Assert.Empty(_ntfy.Messages);
    }

    // Eigene Adresse und Datei: MoonrakerAdapter merkt sich Metadaten pro Adresse und Datei über alle Tests hinweg
    private static string Printing(double progress) => """
        {"result":{"status":{
          "print_stats":{"state":"printing","filename":"wuerfel.gcode","print_duration":600,"message":""},
          "virtual_sdcard":{"progress":PROGRESS},
          "display_status":{"progress":PROGRESS},
          "toolhead":{"extruder":"extruder"},
          "extruder":{"temperature":214.6,"target":215}}}}
        """.Replace("PROGRESS", progress.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task<PrinterResponse> CreatePrinterAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("Voron", PrinterType.Moonraker, "http://voron-test.local:7125", null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>())!;
    }

    /// <summary>Wie <see cref="PrinterSyncService"/>: ohne <c>force</c>.</summary>
    private async Task BackgroundSyncAsync(string printerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Filomat.Api.Data.FilomatDbContext>();
        var printer = (await db.Printers.FindAsync(printerId))!;
        await scope.ServiceProvider.GetRequiredService<ConsumptionSync>().SyncAsync(printer);
    }
}
