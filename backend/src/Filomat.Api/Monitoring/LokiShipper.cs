using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Filomat.Api.Monitoring;

/// <summary>
/// Schickt Logzeilen gesammelt an Loki (<c>/loki/api/v1/push</c>), nach MyJourneys Versender. Ist Loki nicht erreichbar
/// oder die Warteschlange voll, werden Zeilen verworfen statt zu warten; <c>docker compose logs</c> bleibt, wie es ist.
/// Ein eigener HttpClient ohne Logging-Handler, sonst erzeugte jeder Push neue Zeilen für den nächsten.
/// </summary>
public sealed class LokiShipper : IDisposable
{
    internal sealed record Entry(DateTimeOffset At, string Level, string Source, string Line);

    private const int QueueCapacity = 2048;
    internal const int MaxBatchSize = 500;
    internal const string App = "filomat";

    private readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private readonly HttpClient _http;
    private readonly string _pushUrl;
    private readonly string _host;

    public LokiShipper(MonitoringOptions options, HttpMessageHandler? handler = null)
    {
        _pushUrl = options.LokiPushUrl ?? throw new ArgumentException("Loki ist nicht konfiguriert.", nameof(options));
        _host = options.LokiHost;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    internal ChannelReader<Entry> Reader => _queue.Reader;

    public void Enqueue(DateTimeOffset at, string level, string source, string line) =>
        _queue.Writer.TryWrite(new Entry(at, level, source, line));

    /// <summary>Schickt bis zu <see cref="MaxBatchSize"/> Zeilen als einen Push; bei einem Fehler <c>false</c>, die Zeilen sind dann weg.</summary>
    internal async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        while (entries.Count < MaxBatchSize && _queue.Reader.TryRead(out var entry)) entries.Add(entry);
        if (entries.Count == 0) return true;

        try
        {
            using var content = new StringContent(BuildPayload(entries, _host), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_pushUrl, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ein Stream je Level und Quelle; Labels nur app, host, level, source, Zeit in Nanosekunden aufsteigend.</summary>
    internal static string BuildPayload(IReadOnlyList<Entry> entries, string host)
    {
        var streams = entries
            .GroupBy(e => (e.Level, e.Source))
            .Select(group => new
            {
                stream = new Dictionary<string, string>
                {
                    ["app"] = App,
                    ["host"] = host,
                    ["level"] = group.Key.Level,
                    ["source"] = group.Key.Source,
                },
                values = group.OrderBy(e => e.At)
                    .Select(e => new[] { (e.At.ToUnixTimeMilliseconds() * 1_000_000L).ToString(), e.Line })
                    .ToArray(),
            })
            .ToArray();
        return JsonSerializer.Serialize(new { streams });
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Sammelt zwei Sekunden, schickt dann den Stapel; beim Herunterfahren den Rest mit knappem Zeitfenster.</summary>
public sealed class LokiShipperService(LokiShipper shipper) : BackgroundService
{
    private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await shipper.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchDelay, stoppingToken);
                await shipper.FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (shipper.Reader.TryPeek(out _) && await shipper.FlushAsync(cts.Token))
        {
        }
    }
}
