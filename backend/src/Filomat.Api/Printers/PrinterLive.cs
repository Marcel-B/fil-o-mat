using System.Collections.Concurrent;
using System.Text.Json;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Zustand eines Druckers mit den Spulen der App und Hinweisen.</summary>
/// <param name="UpdatedAt">Zeitpunkt der Abfrage beim Drucker; <c>null</c>, wenn er noch nie abgefragt wurde.</param>
public record PrinterLiveResponse(
    string PrinterId,
    string Name,
    PrinterType Type,
    DateTime? UpdatedAt,
    PrinterStatus Status,
    IReadOnlyList<ToolLive> Tools,
    IReadOnlyList<Hint> Hints);

/// <summary>Zuletzt gemeldeter Zustand pro Drucker, im Speicher (nach einem Neustart fragt der nächste Abgleich neu).</summary>
public class PrinterStatusCache
{
    public record Entry(PrinterStatus Status, DateTime UpdatedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, (string Key, Thumbnail? Image)> _thumbnails = new();

    /// <summary>Schon verschickte Meldungen, damit jede nur einmal kommt.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _sent = new();

    public Entry? Get(string printerId) => _entries.GetValueOrDefault(printerId);

    /// <summary>Speichert den Zustand und gibt den vorigen zurück.</summary>
    public Entry? Set(string printerId, PrinterStatus status, DateTime at)
    {
        Entry? previous = null;
        _entries.AddOrUpdate(printerId, _ => new Entry(status, at), (_, old) =>
        {
            previous = old;
            return new Entry(status, at);
        });
        return previous;
    }

    public void Remove(string printerId)
    {
        _entries.TryRemove(printerId, out _);
        _thumbnails.TryRemove(printerId, out _);
    }

    public SemaphoreSlim LockFor(string printerId) => _locks.GetOrAdd(printerId, _ => new SemaphoreSlim(1, 1));

    public bool TryGetThumbnail(string printerId, string key, out Thumbnail? image)
    {
        image = null;
        if (!_thumbnails.TryGetValue(printerId, out var cached) || cached.Key != key) return false;
        image = cached.Image;
        return true;
    }

    public void SetThumbnail(string printerId, string key, Thumbnail? image) => _thumbnails[printerId] = (key, image);

    /// <summary><c>true</c> beim ersten Mal für diesen Schlüssel.</summary>
    public bool FirstTime(string key)
    {
        // Alte Einträge nach zwei Tagen vergessen
        if (_sent.Count > 500)
            foreach (var old in _sent.Where(e => e.Value < DateTime.UtcNow.AddDays(-2)).ToList()) _sent.TryRemove(old.Key, out _);
        return _sent.TryAdd(key, DateTime.UtcNow);
    }
}

/// <summary>Fragt den Zustand der Drucker ab, verknüpft ihn mit den Spulen und meldet Auffälliges über ntfy.</summary>
public class PrinterLive(
    FilomatDbContext db,
    PrinterStatusCache cache,
    IEnumerable<IPrinterAdapter> adapters,
    Notifier notifier,
    ILogger<PrinterLive> logger)
{
    public IPrinterAdapter AdapterFor(Printer printer) => adapters.FirstOrDefault(a => a.Type == printer.Type)
        ?? throw new InvalidOperationException($"Kein Adapter für Druckertyp {printer.Type}.");

    /// <summary>
    /// Fragt den Zustand beim Drucker ab und speichert ihn. <paramref name="adapter"/> erlaubt dem Abgleich, seine
    /// Adapter-Instanz mitzugeben, die die Meldung aus dem Abgleich noch kennt.
    /// </summary>
    public async Task<PrinterStatus> RefreshAsync(Printer printer, IPrinterAdapter? adapter = null, CancellationToken cancellationToken = default)
    {
        PrinterStatus status;
        try
        {
            status = await (adapter ?? AdapterFor(printer)).GetStatusAsync(printer, cancellationToken)
                     ?? new PrinterStatus(PrintState.Idle);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or PrinterUnavailableException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Zustand von {Printer} nicht abrufbar", printer.Name);
            status = new PrinterStatus(PrintState.Offline, Message: ex.Message);
        }
        var previous = cache.Set(printer.Id, status, DateTime.UtcNow);
        await NotifyAsync(printer, status, previous?.Status, cancellationToken);
        return status;
    }

    /// <summary>Speichert "nicht erreichbar", wenn schon der Abgleich scheiterte.</summary>
    public void SetOffline(Printer printer, string message) =>
        cache.Set(printer.Id, new PrinterStatus(PrintState.Offline, Message: message), DateTime.UtcNow);

    /// <summary>Zustand aller Drucker; was älter als <paramref name="maxAge"/> ist, wird beim Drucker neu abgefragt.</summary>
    public async Task<List<PrinterLiveResponse>> GetAllAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var printers = await db.Printers.OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        // Nacheinander: der DbContext und die Adapter sind nicht für parallele Aufrufe gedacht
        var result = new List<PrinterLiveResponse>();
        foreach (var printer in printers)
            result.Add(await GetAsync(printer, maxAge, cancellationToken));
        return result;
    }

    public async Task<PrinterLiveResponse> GetAsync(Printer printer, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var entry = cache.Get(printer.Id);
        if (printer.Enabled && (entry is null || DateTime.UtcNow - entry.UpdatedAt > maxAge))
        {
            var gate = cache.LockFor(printer.Id);
            await gate.WaitAsync(cancellationToken);
            try
            {
                // Ein paralleler Aufruf hat vielleicht gerade abgefragt
                entry = cache.Get(printer.Id);
                if (entry is null || DateTime.UtcNow - entry.UpdatedAt > maxAge)
                {
                    await RefreshAsync(printer, cancellationToken: cancellationToken);
                    entry = cache.Get(printer.Id);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        return await BuildAsync(printer, entry, cancellationToken);
    }

    private async Task<PrinterLiveResponse> BuildAsync(Printer printer, PrinterStatusCache.Entry? entry, CancellationToken cancellationToken)
    {
        var status = entry?.Status ?? new PrinterStatus(printer.Enabled ? PrintState.Offline : PrintState.Idle,
            Message: printer.Enabled ? "Noch nicht abgefragt." : "Pausiert.");
        status = WithReaderTags(printer, status);
        var placed = await PlacedAsync(printer, cancellationToken);
        var (tools, hints) = PrinterInsights.Build(printer.Type, ToolCount(printer), status, placed);
        return new PrinterLiveResponse(printer.Id, printer.Name, printer.Type, entry?.UpdatedAt, status, tools, hints);
    }

    private async Task<List<PlacedSpool>> PlacedAsync(Printer printer, CancellationToken cancellationToken)
    {
        var spools = await db.Spools.Where(s => s.PrinterId == printer.Id && s.EmptiedAt == null)
            .OrderBy(s => s.Tool).ThenBy(s => s.Number).ToListAsync(cancellationToken);
        var itemIds = spools.Select(s => s.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        return spools.Where(s => items.ContainsKey(s.ItemId)).Select(s => new PlacedSpool(s, items[s.ItemId])).ToList();
    }

    /// <summary>Plättchen, die der NFC-Leser am Drucker meldet, als Tag des Slots (ohne Leser bleibt der Zustand, wie er ist).</summary>
    public static PrinterStatus WithReaderTags(Printer printer, PrinterStatus status)
    {
        if (PrinterReader.Slots(printer, DateTime.UtcNow) is not { } tags) return status;
        var slots = status.Slots?.ToList() ?? [];
        var count = Math.Max(ToolCount(printer), slots.Count);
        for (var tool = 0; tool < Math.Min(tags.Count, count); tool++)
        {
            if (tags[tool] is not { } tag) continue;
            var index = slots.FindIndex(s => s.Tool == tool);
            if (index < 0) slots.Add(new SlotReport(tool, null, null, null, TagUid: tag));
            else if (slots[index].TagUid is null) slots[index] = slots[index] with { TagUid = tag };
        }
        return status with { Slots = slots.OrderBy(s => s.Tool).ToList() };
    }

    public static int ToolCount(Printer printer) => printer.ToolCount ?? (printer.Type == PrinterType.SnapmakerU1 ? 4 : 1);

    public async Task<Thumbnail?> GetThumbnailAsync(Printer printer, CancellationToken cancellationToken = default)
    {
        var key = cache.Get(printer.Id)?.Status.JobKey ?? "";
        if (cache.TryGetThumbnail(printer.Id, key, out var cached)) return cached;
        var image = await AdapterFor(printer).GetThumbnailAsync(printer, cancellationToken);
        cache.SetThumbnail(printer.Id, key, image);
        return image;
    }

    /// <summary>Fehler und Spulen, die nicht reichen, je einmal pro Druck.</summary>
    private async Task NotifyAsync(Printer printer, PrinterStatus status, PrinterStatus? previous, CancellationToken cancellationToken)
    {
        if (status.State == PrintState.Error && previous is { State: not PrintState.Error })
            await notifier.SendAsync($"{printer.Name}: Fehler",
                status.Message ?? (status.JobName is { } job ? $"Der Druck {job} braucht Aufmerksamkeit." : "Der Drucker braucht Aufmerksamkeit."),
                "warning", 4, cancellationToken: cancellationToken);

        if (!status.IsActive || status.JobGramsPerTool is null) return;
        var placed = await PlacedAsync(printer, cancellationToken);
        var (_, hints) = PrinterInsights.Build(printer.Type, ToolCount(printer), status, placed);
        foreach (var hint in hints.Where(h => h.Kind == HintKind.SpoolShort))
        {
            if (!cache.FirstTime($"short|{printer.Id}|{status.JobKey ?? status.JobName}|{hint.Tool}")) continue;
            await notifier.SendAsync($"{printer.Name}: Spule reicht nicht", $"{status.JobName}: {hint.Text}", "warning", 4,
                cancellationToken: cancellationToken);
        }
    }
}
