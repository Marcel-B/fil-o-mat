using System.Collections.Concurrent;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>
/// Hält den Zustand der Drucker frisch, solange jemand <c>/api/printers/events</c> offen hat. Drucker, die selbst melden
/// (<see cref="IPrinterPush"/>: Moonraker/U1 per WebSocket, Bambu per MQTT), werden nach jeder Meldung abgefragt,
/// höchstens alle <c>Filomat:LivePushInterval</c> (2 s). Alle anderen, und gepushte Drucker ohne Verbindung, fragt der
/// Dienst alle <c>Filomat:LiveInterval</c> (5 s) ab. Hört niemand mehr zu, schließt er die Verbindungen nach
/// <see cref="Linger"/> wieder; dann bleibt es beim Abgleich alle zwei Minuten.
/// </summary>
public class PrinterLiveService(
    IServiceScopeFactory scopes,
    PrinterEvents events,
    IConfiguration configuration,
    ILogger<PrinterLiveService> logger) : BackgroundService
{
    /// <summary>So lange bleiben die Verbindungen offen, nachdem der letzte Client gegangen ist (Seitenwechsel, Neuladen).</summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(2);

    /// <summary>Drucker-ID → laufende Verbindung zum Drucker.</summary>
    private readonly ConcurrentDictionary<string, Watcher> _watchers = new();

    private sealed class Watcher(string signature, CancellationTokenSource stop)
    {
        public string Signature => signature;
        public CancellationTokenSource Stop => stop;
        public Task Task { get; set; } = Task.CompletedTask;

        /// <summary>Die Verbindung steht und hat schon etwas gemeldet.</summary>
        public volatile bool Connected;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Filomat:LiveInterval", TimeSpan.FromSeconds(5));
        if (interval <= TimeSpan.Zero) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await events.WaitForListenerAsync(stoppingToken);
                logger.LogDebug("Live-Zustand: Clients hören zu, Drucker werden laufend abgefragt");
                var idleSince = (DateTime?)null;
                using var timer = new PeriodicTimer(interval);
                do
                {
                    if (events.ListenerCount > 0) idleSince = null;
                    else if ((idleSince ??= DateTime.UtcNow) < DateTime.UtcNow - Linger) break;
                    try
                    {
                        await TickAsync(interval, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Live-Zustand der Drucker fehlgeschlagen");
                    }
                } while (await timer.WaitForNextTickAsync(stoppingToken));
                await StopWatchersAsync(_watchers.Keys);
                logger.LogDebug("Live-Zustand: niemand hört mehr zu");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopWatchersAsync(_watchers.Keys);
        }
    }

    /// <summary>Verbindungen an die aktuellen Drucker anpassen und alle ohne Verbindung abfragen.</summary>
    private async Task TickAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FilomatDbContext>();
        var live = scope.ServiceProvider.GetRequiredService<PrinterLive>();
        var printers = await db.Printers.AsNoTracking().Where(p => p.Enabled).ToListAsync(cancellationToken);

        // Gelöschte, pausierte oder geänderte Drucker: Verbindung schließen (geänderte bekommen gleich eine neue)
        await StopWatchersAsync(_watchers.Where(w => printers.All(p => p.Id != w.Key || Signature(p) != w.Value.Signature))
            .Select(w => w.Key).ToList());

        foreach (var printer in printers)
        {
            var adapter = live.AdapterFor(printer);
            if (!_watchers.ContainsKey(printer.Id) && adapter is IPrinterPush)
                StartWatcher(printer, cancellationToken);
            if (_watchers.TryGetValue(printer.Id, out var watcher) && watcher.Connected) continue;
            // Etwas Luft, damit der Takt nicht an der Grenze jedes zweite Mal ausfällt
            var maxAge = (adapter.MinLiveInterval > interval ? adapter.MinLiveInterval : interval) * 0.8;
            await live.EnsureFreshAsync(printer, maxAge, cancellationToken);
        }
    }

    private static string Signature(Printer printer) => $"{printer.Type}|{printer.BaseUrl}|{printer.ApiKey}";

    private void StartWatcher(Printer printer, CancellationToken cancellationToken)
    {
        var watcher = new Watcher(Signature(printer), CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        watcher.Task = Task.Run(() => WatchAsync(printer, watcher, watcher.Stop.Token), CancellationToken.None);
        _watchers[printer.Id] = watcher;
    }

    /// <summary>Verbindung halten und nach Abbrüchen neu aufbauen, mit wachsender Pause.</summary>
    private async Task WatchAsync(Printer printer, Watcher self, CancellationToken cancellationToken)
    {
        var pushInterval = configuration.GetValue("Filomat:LivePushInterval", TimeSpan.FromSeconds(2));
        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            // Meldungen kommen bei Temperaturen mehrmals pro Sekunde; abgefragt wird höchstens alle pushInterval
            var dirty = new SemaphoreSlim(0, 1);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var refresher = RefreshLoopAsync(printer, dirty, pushInterval, connection.Token);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var push = (IPrinterPush)scope.ServiceProvider.GetRequiredService<PrinterLive>().AdapterFor(printer);
                await push.WatchAsync(printer, () =>
                {
                    self.Connected = true;
                    delay = TimeSpan.FromSeconds(5);
                    lock (dirty)
                        if (dirty.CurrentCount == 0) dirty.Release();
                }, cancellationToken);
                logger.LogDebug("Verbindung zu {Printer} beendet", printer.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ausgeschaltete Drucker sind normal; den Zustand meldet dann die Abfrage im Takt
                logger.LogDebug(ex, "Keine Live-Verbindung zu {Printer}", printer.Name);
            }
            finally
            {
                self.Connected = false;
                await connection.CancelAsync();
                await refresher;
            }
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
        }
    }

    private async Task RefreshLoopAsync(Printer printer, SemaphoreSlim dirty, TimeSpan pushInterval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await dirty.WaitAsync(cancellationToken);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<FilomatDbContext>();
                    // Frisch laden: Abfragen ändern womöglich den gemerkten Zustand des Adapters
                    if (await db.Printers.FindAsync([printer.Id], cancellationToken) is not { Enabled: true } current) return;
                    await scope.ServiceProvider.GetRequiredService<PrinterLive>().EnsureFreshAsync(current, TimeSpan.Zero, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Zustand von {Printer} nach Meldung nicht abgefragt", printer.Name);
                }
                await Task.Delay(pushInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StopWatchersAsync(IEnumerable<string> printerIds)
    {
        foreach (var id in printerIds.ToList())
        {
            if (!_watchers.TryRemove(id, out var watcher)) continue;
            await watcher.Stop.CancelAsync();
            try
            {
                await watcher.Task;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Live-Verbindung beendet");
            }
            watcher.Stop.Dispose();
        }
    }
}
