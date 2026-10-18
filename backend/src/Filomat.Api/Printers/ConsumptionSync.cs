using Filomat.Api.Data;
using Filomat.Api.Domain;
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
            var jobs = await adapter.GetFinishedJobsAsync(printer, since, cancellationToken);
            foreach (var job in jobs)
            {
                // Drucke vor dem Anlegen des Druckers gehören nicht zum Bestand
                if (job.EndedAt < printer.CreatedAt || string.IsNullOrEmpty(job.JobId)) continue;
                if (await db.Consumptions.AnyAsync(c => c.PrinterId == printer.Id && c.JobId == job.JobId, cancellationToken))
                    continue;

                await RecordAsync(printer, job, cancellationToken);
                recorded++;
                if (printer.LastJobEndedAt is null || job.EndedAt > printer.LastJobEndedAt)
                    printer.LastJobEndedAt = job.EndedAt;
            }
            printer.LastSyncError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
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

    /// <summary>Zieht Gramm vom Restgewicht ab; ohne Restgewicht wird von der vollen Spule ausgegangen.</summary>
    public static void Deduct(Item item, double grams)
    {
        var current = item.RemainingWeight ?? item.NetWeight;
        if (current is null) return;
        item.RemainingWeight = Math.Max(0, Math.Round(current.Value - grams, 2));
    }
}
