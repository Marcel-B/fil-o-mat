using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Anbrechen, Einlegen und Abbuchen von Spulen.</summary>
public class SpoolStock(FilomatDbContext db)
{
    /// <summary>Bricht eine Spule aus dem Vorrat an und legt sie optional in einen Drucker-Kopf oder einen Container.</summary>
    public async Task<Spool> OpenAsync(Item item, string? printerId = null, int? tool = null, double? remainingWeight = null,
        CancellationToken cancellationToken = default, string? containerId = null)
    {
        if (printerId is not null) await ClearLocationAsync(printerId, tool ?? 0, cancellationToken);
        var number = await db.Spools.Where(s => s.ItemId == item.Id).MaxAsync(s => (int?)s.Number, cancellationToken) ?? 0;
        var spool = new Spool
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = item.Id,
            Number = number + 1,
            RemainingWeight = remainingWeight ?? item.NetWeight,
            PrinterId = printerId,
            Tool = printerId is null ? null : tool ?? 0,
            ContainerId = printerId is null ? containerId : null,
            OpenedAt = DateTime.UtcNow,
        };
        if (item.SpoolsInStock is > 0) item.SpoolsInStock--;
        db.Spools.Add(spool);
        await db.SaveChangesAsync(cancellationToken);
        return spool;
    }

    /// <summary>
    /// Legt eine Spule in einen Drucker-Kopf oder (mit <c>printerId = null</c>) ins Lager, dort optional in einen Container.
    /// Eine Spule, die im Zielkopf steckt, wandert ins Lager.
    /// </summary>
    public async Task PlaceAsync(Spool spool, string? printerId, int? tool, CancellationToken cancellationToken = default,
        string? containerId = null)
    {
        if (printerId is not null && (spool.PrinterId != printerId || spool.Tool != (tool ?? 0)))
            await ClearLocationAsync(printerId, tool ?? 0, cancellationToken);
        spool.PrinterId = printerId;
        spool.Tool = printerId is null ? null : tool ?? 0;
        spool.ContainerId = printerId is null ? containerId : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkEmptyAsync(Spool spool, CancellationToken cancellationToken = default)
    {
        spool.EmptiedAt = DateTime.UtcNow;
        spool.PrinterId = null;
        spool.Tool = null;
        spool.ContainerId = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Die Spule der Sorte, die gerade in diesem Kopf steckt. Steckt dort keine (oder eine andere Sorte), wird die einzige
    /// angebrochene Spule der Sorte aus dem Lager eingelegt. Liegen mehrere im Lager, ist unklar, welche es ist: dann
    /// <c>null</c>, und die App fragt nach. Eine neue wird nur angebrochen, wenn die Sorte noch gar keine angebrochene hat.
    /// </summary>
    public async Task<Spool?> FindAsync(Item item, string printerId, int tool, CancellationToken cancellationToken = default)
    {
        var current = await At(printerId, tool).FirstOrDefaultAsync(cancellationToken);
        if (current?.ItemId == item.Id) return current;

        var onShelf = await LooseAsync(item, cancellationToken);
        if (onShelf.Count == 1)
        {
            await PlaceAsync(onShelf[0], printerId, tool, cancellationToken);
            return onShelf[0];
        }
        if (onShelf.Count > 1 || await db.Spools.AnyAsync(s => s.ItemId == item.Id && s.EmptiedAt == null, cancellationToken))
            return null;
        return await OpenAsync(item, printerId, tool, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Wie <see cref="FindAsync"/>, aber für einen beendeten Druck: steckt die Sorte nur in anderen Köpfen, wurde offenbar
    /// eine neue Spule angebrochen. Bei mehreren Spulen im Lager bleibt es bei <c>null</c>.
    /// </summary>
    public async Task<Spool?> ResolveAsync(Item item, string printerId, int tool, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(item, printerId, tool, cancellationToken) is { } spool) return spool;
        if ((await LooseAsync(item, cancellationToken)).Count > 1) return null;
        return await OpenAsync(item, printerId, tool, cancellationToken: cancellationToken);
    }

    /// <summary>Angebrochene Spulen der Sorte, die in keinem Drucker stecken.</summary>
    public Task<List<Spool>> LooseAsync(Item item, CancellationToken cancellationToken = default) =>
        db.Spools.Where(s => s.ItemId == item.Id && s.EmptiedAt == null && s.PrinterId == null)
            .OrderBy(s => s.Number).ToListAsync(cancellationToken);

    /// <summary>
    /// Zieht Gramm von der Spule ab. Reicht sie nicht und liegt Vorrat bereit, wurde offenbar im selben Kopf eine neue Spule
    /// eingelegt (das Tag auf dem Musterplättchen bleibt dasselbe): die alte gilt als leer, der Rest geht von der neuen ab.
    /// </summary>
    /// <returns>Die Spule, die danach im Kopf steckt.</returns>
    public async Task<Spool> DeductAsync(Spool spool, Item item, double grams, CancellationToken cancellationToken = default)
    {
        var current = spool.RemainingWeight ?? item.NetWeight;
        if (current is null) return spool;

        var remaining = current.Value - grams;
        while (remaining < 0 && item.SpoolsInStock is > 0 && item.NetWeight is > 0)
        {
            var (printerId, tool) = (spool.PrinterId, spool.Tool);
            spool.RemainingWeight = 0;
            await MarkEmptyAsync(spool, cancellationToken);
            spool = await OpenAsync(item, printerId, tool, item.NetWeight.Value + remaining, cancellationToken);
            remaining = spool.RemainingWeight!.Value;
        }
        spool.RemainingWeight = Math.Max(0, Math.Round(remaining, 2));
        await db.SaveChangesAsync(cancellationToken);
        return spool;
    }

    private IQueryable<Spool> At(string printerId, int tool) =>
        db.Spools.Where(s => s.PrinterId == printerId && s.Tool == tool && s.EmptiedAt == null);

    private async Task ClearLocationAsync(string printerId, int tool, CancellationToken cancellationToken)
    {
        foreach (var other in await At(printerId, tool).ToListAsync(cancellationToken))
        {
            other.PrinterId = null;
            other.Tool = null;
        }
    }
}
