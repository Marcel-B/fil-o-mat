using Filomat.Api.Data;
using Filomat.Api.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Holt beendete Drucke vom Drucker und bucht den Verbrauch auf die eingelegte Spule.</summary>
public class ConsumptionSync(FilomatDbContext db, IEnumerable<IPrinterAdapter> adapters, ILogger<ConsumptionSync> logger)
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
            // Drucke den Spulen zuordnen, die beim letzten Abgleich (also während des Drucks) eingelegt waren
            var toolsDuringPrint = ReadToolState(printer) ?? currentTools;
            var jobs = await adapter.GetFinishedJobsAsync(printer, since, cancellationToken);
            foreach (var job in jobs)
            {
                // Drucke vor dem Anlegen des Druckers gehören nicht zum Bestand
                if (job.EndedAt < printer.CreatedAt || string.IsNullOrEmpty(job.JobId)) continue;
                if (await db.Consumptions.AnyAsync(c => c.PrinterId == printer.Id && c.JobId == job.JobId, cancellationToken))
                    continue;

                if (toolsDuringPrint is not null && job.SlicedGramsPerFilament is { Count: > 0 })
                    await RecordPerToolAsync(printer, job, toolsDuringPrint, cancellationToken);
                else
                    await RecordAsync(printer, job, cancellationToken);
                recorded++;
                if (printer.LastJobEndedAt is null || job.EndedAt > printer.LastJobEndedAt)
                    printer.LastJobEndedAt = job.EndedAt;
            }
            if (currentTools is not null)
            {
                printer.ToolStateJson = JsonSerializer.Serialize(currentTools);
                await MoveLoadedSpoolsAsync(printer, currentTools, cancellationToken);
            }
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
        // Abgebucht wird nur, wenn genau eine Spule im Drucker liegt
        var loaded = await db.Items.Where(i => i.ContainerId == printer.ContainerId).ToListAsync(cancellationToken);
        var item = loaded.Count == 1 ? loaded[0] : null;
        var grams = Math.Round(FilamentMath.GramsForJob(job, item?.ItemData?.Type), 2);

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
            RecordedAt = DateTime.UtcNow,
        });
        if (item is not null) Deduct(item, grams);
    }

    /// <summary>Bucht jeden Werkzeugkopf einzeln auf die Spule, deren Tag der Drucker in diesem Kopf erkannt hat.</summary>
    private async Task RecordPerToolAsync(Printer printer, PrintJob job, ToolState tools, CancellationToken cancellationToken)
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
            var extruder = tools.ExtruderFor(i);
            perExtruder[extruder] = perExtruder.TryGetValue(extruder, out var sum)
                ? (sum.Grams + grams, sum.Length + length)
                : (grams, length);
        }

        foreach (var (extruder, usage) in perExtruder)
        {
            var tag = tools.TagFor(extruder);
            var item = tag is null ? null : await db.Items.FirstOrDefaultAsync(i => i.RfidTag == tag, cancellationToken);
            var grams = Math.Round(usage.Grams * ratio, 2);
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
                RecordedAt = DateTime.UtcNow,
            });
            if (item is not null) Deduct(item, grams);
        }
    }

    /// <summary>Spulen, die der Drucker erkennt, liegen ab jetzt im Container des Druckers.</summary>
    private async Task MoveLoadedSpoolsAsync(Printer printer, ToolState tools, CancellationToken cancellationToken)
    {
        var tags = tools.TagUids.OfType<string>().ToList();
        if (tags.Count == 0) return;
        var items = await db.Items.Where(i => tags.Contains(i.RfidTag) && i.ContainerId != printer.ContainerId)
            .ToListAsync(cancellationToken);
        foreach (var item in items) item.ContainerId = printer.ContainerId;
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

    /// <summary>Zieht Gramm vom Restgewicht ab; ohne Restgewicht wird von der vollen Spule ausgegangen.</summary>
    public static void Deduct(Item item, double grams)
    {
        var current = item.RemainingWeight ?? item.NetWeight;
        if (current is null) return;
        item.RemainingWeight = Math.Max(0, Math.Round(current.Value - grams, 2));
    }
}
