using System.Text.Json;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Printers;

/// <summary>Was der Leser über einen Slot weiß.</summary>
/// <param name="Slot">Slot bzw. Kopf, ab 0.</param>
/// <param name="State"><c>empty</c> (kein Plättchen), <c>known</c>, <c>unknown</c> (keinem Filament zugeordnet) oder <c>container</c>.</param>
/// <param name="Label">Kurztext fürs Display des Lesers, z. B. "SUNLU PLA matte #2".</param>
public record ReaderSlot(int Slot, string? Tag, string State, string? Label = null, string? ItemId = null, int? SpoolNumber = null,
    double? RemainingWeight = null);

public record ReaderReport(IReadOnlyList<string?> Slots);

public record ReaderResponse(IReadOnlyList<ReaderSlot> Slots);

/// <param name="Slot">Slot, ab 0, den der Nutzer am Leser ausgewählt hat.</param>
public record ReaderTap(int Slot, string Tag);

/// <summary>
/// NFC-Leser am Drucker (ESP32 mit PN532/RC522): meldet pro Slot, welches Musterplättchen dort steckt. Ein neu
/// eingestecktes Plättchen legt die Spule seiner Sorte in diesen Slot, wie bei den Köpfen der U1.
/// </summary>
public class PrinterReader(FilomatDbContext db, SpoolStock spools, Notifier notifier, SpoolQuestion question)
{
    /// <summary>Der Leser meldet sich jede Minute; länger still gilt er als aus.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public const int MaxSlots = 16;

    public async Task<ReaderResponse> ReportAsync(Printer printer, IReadOnlyList<string?> reported, CancellationToken cancellationToken = default)
    {
        var slots = reported.Take(MaxSlots).Select(NormalizeUid).ToList();
        // Nur Änderungen übernehmen: die Meldung jede Minute soll keine Spule zurücklegen, die in der App umgesteckt wurde
        // Unbekannte Plättchen merkt sich der Leser als "?UID", damit sie nach dem Zuordnen in der App als neu gelten
        var previous = Stored(printer);
        var stored = slots.ToList();
        var result = new List<ReaderSlot>();
        for (var slot = 0; slot < slots.Count; slot++)
        {
            if (slots[slot] is not { } tag)
            {
                result.Add(new ReaderSlot(slot, null, "empty"));
                continue;
            }
            var before = previous is not null && slot < previous.Count ? previous[slot] : null;
            var changed = before != tag;
            var item = await db.Items.WithTag(tag).FirstOrDefaultAsync(cancellationToken);
            if (item is null)
            {
                if (await db.Containers.AnyAsync(c => c.RfidTag == tag, cancellationToken))
                {
                    result.Add(new ReaderSlot(slot, tag, "container", "Container-Tag"));
                    continue;
                }
                result.Add(new ReaderSlot(slot, tag, "unknown", "Unbekannt"));
                stored[slot] = $"?{tag}";
                if (before != stored[slot]) await NotifyUnknownAsync(printer, slot, tag, cancellationToken);
                continue;
            }

            var spool = changed
                ? await LoadAsync(printer, item, slot, slots, cancellationToken)
                : await db.Spools.FirstOrDefaultAsync(s => s.PrinterId == printer.Id && s.Tool == slot && s.ItemId == item.Id
                                                           && s.EmptiedAt == null, cancellationToken);
            if (changed) item.ContainerId = printer.ContainerId;
            result.Add(new ReaderSlot(slot, tag, "known", spool is null ? item.Name : $"{item.Name} #{spool.Number}", item.Id,
                spool?.Number, spool?.RemainingWeight));
        }

        printer.ReaderSlotsJson = JsonSerializer.Serialize(stored);
        printer.ReaderSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ReaderResponse(result);
    }

    /// <summary>
    /// Tipp-Modus (ein Leser für mehrere Slots, z. B. Kobra mit ACE): Plättchen angetippt und Slot am Leser gewählt. Legt die
    /// Spule der Sorte immer in diesen Slot, auch wenn es dasselbe Plättchen wie zuletzt ist.
    /// </summary>
    public async Task<ReaderSlot> TapAsync(Printer printer, int slot, string rawTag, CancellationToken cancellationToken = default)
    {
        var tag = NormalizeUid(rawTag)!;
        printer.ReaderSeenAt = DateTime.UtcNow;
        ReaderSlot result;
        if (await db.Items.WithTag(tag).FirstOrDefaultAsync(cancellationToken) is { } item)
        {
            var spool = await LoadAsync(printer, item, slot, null, cancellationToken);
            item.ContainerId = printer.ContainerId;
            result = new ReaderSlot(slot, tag, "known", spool is null ? item.Name : $"{item.Name} #{spool.Number}", item.Id,
                spool?.Number, spool?.RemainingWeight);
        }
        else if (await db.Containers.AnyAsync(c => c.RfidTag == tag, cancellationToken))
            result = new ReaderSlot(slot, tag, "container", "Container-Tag");
        else
        {
            result = new ReaderSlot(slot, tag, "unknown", "Unbekannt");
            await NotifyUnknownAsync(printer, slot, tag, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Legt die Spule der Sorte in den Slot. Steckt sie schon in einem anderen Slot dieses Druckers (Plättchen umgesteckt),
    /// wandert sie mit, statt dass eine neue angebrochen wird; außer der Leser meldet dort noch ein Plättchen derselben Sorte.
    /// Liegen mehrere Spulen der Sorte lose und ist keine gerade gewogen, wird per ntfy nachgefragt (dann <c>null</c>).
    /// </summary>
    private async Task<Spool?> LoadAsync(Printer printer, Item item, int slot, IReadOnlyList<string?>? reported,
        CancellationToken cancellationToken)
    {
        var inPrinter = await db.Spools
            .Where(s => s.ItemId == item.Id && s.PrinterId == printer.Id && s.EmptiedAt == null)
            .ToListAsync(cancellationToken);
        if (inPrinter.Any(s => s.Tool == slot)) return await FindAsync(printer, item, slot, cancellationToken);

        var tags = item.AllTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var elsewhere = inPrinter
            .Where(s => s.Tool is not { } tool || reported is null || tool >= reported.Count || reported[tool] is not { } other
                        || !tags.Contains(other))
            .ToList();
        if (elsewhere.Count != 1) return await FindAsync(printer, item, slot, cancellationToken);
        await spools.PlaceAsync(elsewhere[0], printer.Id, slot, cancellationToken);
        return elsewhere[0];
    }

    private async Task<Spool?> FindAsync(Printer printer, Item item, int slot, CancellationToken cancellationToken)
    {
        if (await spools.FindAsync(item, printer.Id, slot, cancellationToken) is { } spool) return spool;
        await question.AskAsync(printer, slot, item, cancellationToken);
        return null;
    }

    /// <summary>Zuletzt gemeldete Plättchen pro Slot; <c>null</c>, wenn der Drucker keinen Leser hat oder er still ist.</summary>
    public static IReadOnlyList<string?>? Slots(Printer printer, DateTime now) =>
        printer.ReaderSeenAt is { } seen && now - seen <= MaxAge ? Stored(printer)?.Select(tag => tag?.TrimStart('?')).ToList() : null;

    private static List<string?>? Stored(Printer printer)
    {
        if (string.IsNullOrEmpty(printer.ReaderSlotsJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string?>>(printer.ReaderSlotsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"04:37:8f:53" oder "04-37-8F-53" → "04378F53", wie die Apps die UID speichern.</summary>
    public static string? NormalizeUid(string? raw)
    {
        if (raw is null) return null;
        var hex = new string(raw.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 0 ? null : hex;
    }

    private async Task NotifyUnknownAsync(Printer printer, int slot, string tag, CancellationToken cancellationToken)
    {
        using var language = L.Use(await notifier.LanguageAsync(cancellationToken));
        var place = PrinterInsights.PlaceName(printer.Type, PrinterLive.ToolCount(printer), slot);
        await notifier.SendAsync(L.F("{0}: unbekanntes Plättchen", printer.Name),
            (place.Length > 0 ? $"{place}: " : "") + L.F("Das Plättchen {0} gehört zu keinem Filament.", tag) + " " +
            L.T("In der App unter Filament bearbeiten > Plättchen hinzufügen zuordnen und neu einstecken."),
            "question", cancellationToken: cancellationToken);
    }
}
