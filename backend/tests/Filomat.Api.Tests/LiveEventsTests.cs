using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Filomat.Api.Tests;

/// <summary>Liest Server-Sent Events aus einer offenen Antwort.</summary>
public sealed class EventReader(Stream stream) : IDisposable
{
    private readonly StreamReader _reader = new(stream, Encoding.UTF8);

    public record Event(string Name, string Data);

    /// <summary>Nächstes Ereignis (Kommentare wie <c>: ping</c> und <c>retry</c> zählen nicht).</summary>
    public async Task<Event> NextAsync(TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        string? name = null;
        var data = new StringBuilder();
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancel.Token) ?? throw new EndOfStreamException();
            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0) return new Event(name ?? "message", data.ToString());
                continue;
            }
            if (line.StartsWith("event: ")) name = line[7..];
            else if (line.StartsWith("data: ")) data.Append(line[6..]);
        }
    }

    /// <summary>Wartet auf ein Ereignis mit diesem Namen, das <paramref name="match"/> erfüllt.</summary>
    public async Task<Event> UntilAsync(string name, Func<Event, bool>? match = null, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) throw new TimeoutException($"Kein Ereignis {name}");
            var next = await NextAsync(left);
            if (next.Name == name && (match is null || match(next))) return next;
        }
    }

    public void Dispose() => _reader.Dispose();
}

public class LiveEventsTests : IDisposable
{
    private readonly FakeBambu _bambu = new() { Print = FakeBambu.Report("RUNNING", 10) };
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public LiveEventsTests()
    {
        _factory = new ApiFactory { BambuLan = _bambu, LiveInterval = TimeSpan.FromMilliseconds(200) };
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private async Task<EventReader> OpenAsync()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/printers/events"),
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return new EventReader(await response.Content.ReadAsStreamAsync());
    }

    private static List<PrinterLiveResponse> Printers(EventReader.Event e) =>
        JsonSerializer.Deserialize<List<PrinterLiveResponse>>(e.Data, JsonSerializerOptions.Web)!;

    [Fact]
    public async Task SendsTheStateAtOnceAndChangesAfterWrites()
    {
        using var events = await OpenAsync();
        Assert.Empty(Printers(await events.UntilAsync("status")));

        var response = await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, "04AABBCCDD", "PLA weiß", null, null, new FilamentData { Type = "PLA" }, 1000));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await events.UntilAsync("changed");
    }

    [Fact]
    public async Task FollowsABambuPrinterOverItsOpenConnection()
    {
        var response = await _client.PostAsJsonAsync("/api/printers",
            new PrinterRequest("P1S", PrinterType.Bambu, "http://192.168.2.170", "12345678", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var events = await OpenAsync();
        await _bambu.Watching.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await events.UntilAsync("status", e => Printers(e).Single().Status.Progress == 10);
        var queries = _bambu.Logins.Count;

        _bambu.Print = FakeBambu.Report("RUNNING", 55);
        _bambu.Push();
        var live = Printers(await events.UntilAsync("status", e => Printers(e).Single().Status.Progress == 55)).Single();
        Assert.Equal(PrintState.Printing, live.Status.State);
        // Alles aus der offenen Verbindung, keine eigene Abfrage (pushall) pro Änderung
        Assert.Equal(queries, _bambu.Logins.Count);
    }

    [Fact]
    public void PrinterAddressBecomesTheMoonrakerWebSocket()
    {
        Assert.Equal("ws://192.168.2.155/websocket", MoonrakerAdapter.WebSocketUri("http://192.168.2.155").ToString());
        Assert.Equal("ws://192.168.2.155:7125/websocket", MoonrakerAdapter.WebSocketUri("192.168.2.155:7125/").ToString());
        Assert.Equal("wss://drucker.local/moonraker/websocket", MoonrakerAdapter.WebSocketUri("https://drucker.local/moonraker").ToString());
    }

    /// <summary>Moonraker wie auf dem U1: Abo über JSON-RPC, danach Benachrichtigungen; Klipper-Neustart verlangt ein neues Abo.</summary>
    [Fact]
    public async Task SubscribesToMoonrakerAndReportsChanges()
    {
        var subscriptions = new List<JsonElement>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseWebSockets();
        app.Map("/websocket", async (HttpContext context) =>
        {
            Assert.Equal("geheim", context.Request.Headers["X-Api-Key"]);
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[64 * 1024];

            async Task<JsonElement> ReceiveAsync()
            {
                var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                return JsonDocument.Parse(buffer.AsMemory(0, received.Count)).RootElement.Clone();
            }

            Task SendAsync(string json) => socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, context.RequestAborted);

            subscriptions.Add(await ReceiveAsync());
            await SendAsync("""{"jsonrpc":"2.0","result":{"eventtime":1,"status":{"print_stats":{"state":"printing"}}},"id":1}""");
            // Systemlast zählt nicht als Änderung
            await SendAsync("""{"jsonrpc":"2.0","method":"notify_proc_stat_update","params":[{}]}""");
            await SendAsync("""{"jsonrpc":"2.0","method":"notify_status_update","params":[{"extruder":{"temperature":215.3}},2]}""");
            await SendAsync("""{"jsonrpc":"2.0","method":"notify_klippy_ready"}""");
            subscriptions.Add(await ReceiveAsync());
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        var changes = 0;
        var printer = new Printer { Id = "u1", ContainerId = "c1", Name = "Yuffie", Type = PrinterType.SnapmakerU1, BaseUrl = address, ApiKey = "geheim" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await new SnapmakerU1Adapter(new HttpClient()).WatchAsync(printer, () => Interlocked.Increment(ref changes), timeout.Token);

        // Abo-Antwort, Statusänderung, Klipper bereit
        Assert.Equal(3, changes);
        Assert.Equal(2, subscriptions.Count);
        var subscribe = subscriptions[0];
        Assert.Equal("printer.objects.subscribe", subscribe.GetProperty("method").GetString());
        var objects = subscribe.GetProperty("params").GetProperty("objects");
        Assert.True(objects.TryGetProperty("print_stats", out _));
        Assert.True(objects.TryGetProperty("filament_detect", out _));
        await app.StopAsync();
    }
}
