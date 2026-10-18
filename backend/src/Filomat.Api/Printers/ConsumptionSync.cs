using Filomat.Api.Data;
using Filomat.Api.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Holt beendete Drucke vom Drucker und bucht den Verbrauch auf die eingelegte Spule.</summary>
public class ConsumptionSync(FilomatDbContext db, SpoolStock spools, IEnumerable<IPrinterAdapter> adapters, ILogger<ConsumptionSync> logger)
{
    /// <summary>Überlappung beim Abfragen, damit Drucke mit späterem Ende nicht verloren gehen.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromDays(2);

    public async Task<int> SyncAsync(Printer printer, CancellationToken cancellationToken = default)
    {
        var adapter = adapters.FirstOrDefault(a => a.Type == printer.Type)
            ?? throw new InvalidOperationException($"Kein Adapter für Druckertyp {printer.Type}.");

        var since = (printer.LastJobEndedAt ?? printer.CreatedAt) - Overlap;
        int recorded = 0;
        try
        {
            var currentTools = await adapter.GetToolStateAsync(printer, cancellationToken);
            if (currentTools is not null)
            {
                // Zuerst die erkannten Spulen übernehmen, damit neue Drucke schon auf sie gebucht werden
                await MoveLoadedSpoolsAsync(printer, currentTools, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            // Drucke den Spulen zuordnen, die beim letzten Abgleich (also während des Drucks) eingelegt waren
            var toolsDuringPrint = ReadToolState(printer) ?? currentTools;
            var jobs = await adapter.GetFinishedJobsAsync(printer, since, cancellationToken);
            foreach (var job in jobs)
            {
                // Drucke vor dem Anlegen des Druckers gehören nicht zum Bestand
                if (job.EndedAt < printer.CreatedAt || string.IsNullOrEmpty(job.JobId)) continue;
                if (await db.Consumptions.AnyAsync(c => c.PrinterId == printer.Id && c.JobId == job.JobId, cancellationToken))
                    continue;

                // Pro Kopf buchen, wenn der Drucker die Spulen erkennt oder mehrere Slots meldet (z. B. MMU3)
                if (job.SlicedGramsPerFilament is { Count: > 0 } perFilament && (toolsDuringPrint is not null || perFilament.Count > 1))
                    await RecordPerToolAsync(printer, job, toolsDuringPrint, cancellationToken);
                else
                    await RecordAsync(printer, job, cancellationToken);
                recorded++;
                if (printer.LastJobEndedAt is null || job.EndedAt > printer.LastJobEndedAt)
                    printer.LastJobEndedAt = job.EndedAt;
            }
            if (currentTools is not null) printer.ToolStateJson = JsonSerializer.Serialize(currentTools);
            printer.LastSyncError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Abgleich mit Drucker {Printer} fehlgeschlagen", printer.Name);
            printer.LastSyncError = ex.Message;
        }

        printer.LastSyncAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    private async Task RecordAsync(Printer printer, PrintJob job, CancellationToken cancellationToken)
    {
        var item = await ItemAtToolAsync(printer, 0, fallbackToContainer: true, cancellationToken);
        var grams = Math.Round(FilamentMath.GramsForJob(job, item?.ItemData?.Type), 2);

        var spool = item is null ? null : await BookAsync(item, printer.Id, 0, grams, cancellationToken);
        db.Consumptions.Add(new Consumption
        {
            Id = Guid.NewGuid().ToString(),
            PrinterId = printer.Id,
            JobId = job.JobId,
            JobName = job.Name,
            Status = job.Status,
            EndedAt = job.EndedAt,
            LengthMm = job.LengthMm,
            Grams = grams,
            ItemId = item?.Id,
            SpoolId = spool?.Id,
            RecordedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Bucht auf die Spule der Sorte im Kopf und gibt die Spule zurück, von der abgebucht wurde.</summary>
    private async Task<Spool> BookAsync(Item item, string printerId, int tool, double grams, CancellationToken cancellationToken)
    {
        var spool = await spools.ResolveAsync(item, printerId, tool, cancellationToken);
        await spools.DeductAsync(spool, item, grams, cancellationToken);
        return spool;
    }

    /// <summary>
    /// Sorte der Spule, die an diesem Kopf des Druckers steckt. Sonst, falls erlaubt, die einzige Sorte im
    /// Drucker-Container; bei keiner oder mehreren bleibt die Buchung offen.
    /// </summary>
    private async Task<Item?> ItemAtToolAsync(Printer printer, int tool, bool fallbackToContainer, CancellationToken cancellationToken)
    {
        var placed = await db.Spools
            .Where(s => s.PrinterId == printer.Id && s.EmptiedAt == null && (s.Tool ?? 0) == tool)
            .Select(s => s.ItemId)
            .ToListAsync(cancellationToken);
        if (placed.Count == 1) return await db.Items.FindAsync([placed[0]], cancellationToken);
        if (!fallbackToContainer) return null;
        var loaded = await db.Items.Where(i => i.ContainerId == printer.ContainerId).ToListAsync(cancellationToken);
        return loaded.Count == 1 ? loaded[0] : null;
    }

    /// <summary>
    /// Bucht jeden Werkzeugkopf einzeln: auf die Spule, deren Tag der Drucker in diesem Kopf erkannt hat, oder
    /// (ohne Tag-Erkennung, z. B. MMU3) auf die Spule, die in der App an diesem Kopf eingelegt ist.
    /// </summary>
    private async Task RecordPerToolAsync(Printer printer, PrintJob job, ToolState? tools, CancellationToken cancellationToken)
    {
        // Abgebrochene Drucke: anteilig nach tatsächlich verbrauchter Länge
        var ratio = job.SlicedLengthMm is > 0 && job.LengthMm > 0
            ? Math.Min(1, job.LengthMm / job.SlicedLengthMm.Value)
            : job.Status == "completed" ? 1 : 0;

        var perExtruder = new SortedDictionary<int, (double Grams, double Length)>();
        for (var i = 0; i < job.SlicedGramsPerFilament!.Count; i++)
        {
            var grams = job.SlicedGramsPerFilament[i];
            if (grams <= 0) continue;
            var length = job.SlicedLengthPerFilament is { } lengths && i < lengths.Count ? lengths[i] : 0;
            var extruder = tools?.ExtruderFor(i) ?? i;
            perExtruder[extruder] = perExtruder.TryGetValue(extruder, out var sum)
                ? (sum.Grams + grams, sum.Length + length)
                : (grams, length);
        }

        foreach (var (extruder, usage) in perExtruder)
        {
            var tag = tools?.TagFor(extruder);
            var item = tools is null
                ? await ItemAtToolAsync(printer, extruder, fallbackToContainer: perExtruder.Count == 1, cancellationToken)
                : tag is null ? null : await db.Items.FirstOrDefaultAsync(i => i.RfidTag == tag, cancellationToken);
            var grams = Math.Round(usage.Grams * ratio, 2);
            var spool = item is null ? null : await BookAsync(item, printer.Id, extruder, grams, cancellationToken);
            db.Consumptions.Add(new Consumption
            {
                Id = Guid.NewGuid().ToString(),
                PrinterId = printer.Id,
                JobId = job.JobId,
                JobName = job.Name,
                Status = job.Status,
                EndedAt = job.EndedAt,
                LengthMm = Math.Round(usage.Length * ratio, 1),
                Grams = grams,
                Tool = extruder,
                TagUid = tag,
                ItemId = item?.Id,
                SpoolId = spool?.Id,
                RecordedAt = DateTime.UtcNow,
            });
        }
    }

    /// <summary>
    /// Spulen, die der Drucker erkennt, liegen ab jetzt im Container des Druckers. Unbekannte Tags werden
    /// mit den Daten angelegt, die der Drucker vom Tag gelesen hat, und offene Buchungen dieses Tags zugeordnet.
    /// </summary>
    private async Task MoveLoadedSpoolsAsync(Printer printer, ToolState tools, CancellationToken cancellationToken)
    {
        for (var tool = 0; tool < tools.TagUids.Count; tool++)
        {
            if (tools.TagUids[tool] is not { } tag) continue;
            var item = await db.Items.FirstOrDefaultAsync(i => i.RfidTag == tag, cancellationToken);
            if (item is null)
            {
                // Tags von Containern gehören nicht in einen Drucker
                if (await db.Containers.AnyAsync(c => c.RfidTag == tag, cancellationToken)) continue;
                var filament = tools.Filaments is { } list && tool < list.Count ? list[tool] : null;
                item = NewItemFromTag(tag, filament, tool);
                db.Items.Add(item);
                var open = await db.Consumptions.Where(c => c.TagUid == tag && c.ItemId == null).ToListAsync(cancellationToken);
                foreach (var entry in open) entry.ItemId = item.Id;
            }
            item.ContainerId = printer.ContainerId;
            // Plättchen am Kopf = eine Spule dieser Sorte steckt dort
            await spools.ResolveAsync(item, printer.Id, tool, cancellationToken);
        }
    }

    private static Item NewItemFromTag(string tag, DetectedFilament? filament, int tool)
    {
        var name = string.Join(" ", new[] { filament?.Vendor, filament?.Type, filament?.SubType }.Where(s => s is not null));
        return new Item
        {
            Id = Guid.NewGuid().ToString(),
            RfidTag = tag,
            Name = name.Length > 0 ? name : $"Spule aus Kopf {tool + 1}",
            Description = "Vom Drucker erkannt",
            CreatedAt = DateTime.UtcNow,
            ItemData = filament?.Type is null ? null : new FilamentData
            {
                Protocol = "openspool",
                Version = "1.0",
                Type = filament.Type,
                Brand = filament.Vendor,
                Variant = filament.SubType,
                ColorHex = filament.ColorHex,
                MinTemp = filament.MinTemp?.ToString(),
                MaxTemp = filament.MaxTemp?.ToString(),
                BedMinTemp = filament.BedTemp?.ToString(),
                BedMaxTemp = filament.BedTemp?.ToString(),
            },
        };
    }

    private static ToolState? ReadToolState(Printer printer)
    {
        if (string.IsNullOrEmpty(printer.ToolStateJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<ToolState>(printer.ToolStateJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
