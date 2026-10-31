using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

/// <summary>Container im Datenformat der Apps (Zeitstempel in Millisekunden seit 1970).</summary>
public record SnapshotContainer(
    string Id,
    string RfidTag,
    string Name,
    string? Description,
    int? Capacity,
    long CreatedAt,
    bool IsStandard = false);

/// <summary>Filament im Datenformat der Apps.</summary>
public record SnapshotItem(
    string Id,
    string RfidTag,
    string Name,
    string? Description,
    long CreatedAt,
    string? ContainerId,
    FilamentData? ItemData,
    double? NetWeight = null,
    double? SpoolWeight = null,
    int? SpoolsInStock = null,
    double? TransmissionDistance = null,
    List<string>? ExtraTags = null);

/// <summary>Angebrochene Spule im Datenformat der Apps; wird nur vom Backend gepflegt.</summary>
public record SnapshotSpool(
    string Id,
    string ItemId,
    int Number,
    double? RemainingWeight,
    string? PrinterId,
    int? Tool,
    long OpenedAt,
    string? ContainerId = null);

public record Snapshot(List<SnapshotContainer> Containers, List<SnapshotItem> Items, List<SnapshotSpool>? Spools = null);

public record ImportResult(int Containers, int Items, List<string> Skipped);

public static class SyncEndpoints
{
    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/snapshot", async (FilomatDbContext db) => new Snapshot(
            (await db.Containers.OrderBy(c => c.CreatedAt).ToListAsync()).Select(c => new SnapshotContainer(
                c.Id, c.RfidTag, c.Name, c.Description, c.Capacity, ToMillis(c.CreatedAt), c.IsStandard)).ToList(),
            (await db.Items.OrderBy(i => i.CreatedAt).ToListAsync()).Select(i => new SnapshotItem(
                i.Id, i.RfidTag, i.Name, i.Description, ToMillis(i.CreatedAt), i.ContainerId, i.ItemData,
                i.NetWeight, i.SpoolWeight, i.SpoolsInStock, i.TransmissionDistance, i.ExtraTags)).ToList(),
            (await db.Spools.Where(s => s.EmptiedAt == null).OrderBy(s => s.OpenedAt).ToListAsync()).Select(s => new SnapshotSpool(
                s.Id, s.ItemId, s.Number, s.RemainingWeight, s.PrinterId, s.Tool, ToMillis(s.OpenedAt), s.ContainerId)).ToList()))
            .WithTags("Abgleich");

        api.MapPost("/import", ImportAsync).WithTags("Abgleich");
        return api;
    }

    /// <summary>
    /// Übernimmt die Daten einer App: neue Einträge werden angelegt, vorhandene (gleiche ID) aktualisiert.
    /// Nichts wird gelöscht. Einträge, deren Tag schon zu etwas anderem gehört, werden übersprungen.
    /// </summary>
    private static async Task<ImportResult> ImportAsync(Snapshot snapshot, FilomatDbContext db)
    {
        var skipped = new List<string>();
        int containers = 0, items = 0;

        var containerByTag = await db.Containers.ToDictionaryAsync(c => c.RfidTag, c => c.Id);
        var itemByTag = new Dictionary<string, string>();
        foreach (var item in await db.Items.ToListAsync())
            foreach (var tag in item.AllTags) itemByTag.TryAdd(tag, item.Id);

        foreach (var incoming in snapshot.Containers ?? [])
        {
            if (string.IsNullOrWhiteSpace(incoming.Id) || string.IsNullOrWhiteSpace(incoming.RfidTag)) continue;
            var existing = await db.Containers.FindAsync(incoming.Id);
            if (existing?.IsStandard == true) continue;

            if ((containerByTag.TryGetValue(incoming.RfidTag, out var owner) && owner != incoming.Id)
                || itemByTag.ContainsKey(incoming.RfidTag))
            {
                skipped.Add(L.F("Container '{0}': Tag {1} ist bereits vergeben.", incoming.Name, incoming.RfidTag));
                continue;
            }

            if (existing is null)
            {
                existing = new Container
                {
                    Id = incoming.Id,
                    RfidTag = incoming.RfidTag,
                    Name = incoming.Name,
                    CreatedAt = FromMillis(incoming.CreatedAt),
                };
                db.Containers.Add(existing);
            }
            existing.RfidTag = incoming.RfidTag;
            existing.Name = incoming.Name;
            existing.Description = incoming.Description ?? "";
            existing.Capacity = incoming.Capacity;
            containerByTag[incoming.RfidTag] = incoming.Id;
            containers++;
        }
        await db.SaveChangesAsync();

        var containerIds = (await db.Containers.Select(c => c.Id).ToListAsync()).ToHashSet();
        foreach (var incoming in snapshot.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(incoming.Id) || string.IsNullOrWhiteSpace(incoming.RfidTag)) continue;
            if ((itemByTag.TryGetValue(incoming.RfidTag, out var owner) && owner != incoming.Id)
                || containerByTag.ContainsKey(incoming.RfidTag))
            {
                skipped.Add(L.F("Filament '{0}': Tag {1} ist bereits vergeben.", incoming.Name, incoming.RfidTag));
                continue;
            }

            var existing = await db.Items.FindAsync(incoming.Id);
            if (existing is null)
            {
                existing = new Item
                {
                    Id = incoming.Id,
                    RfidTag = incoming.RfidTag,
                    Name = incoming.Name,
                    CreatedAt = FromMillis(incoming.CreatedAt),
                };
                db.Items.Add(existing);
            }
            existing.RfidTag = incoming.RfidTag;
            existing.Name = incoming.Name;
            existing.Description = incoming.Description ?? "";
            existing.ContainerId = incoming.ContainerId is { } id && containerIds.Contains(id) ? id : null;
            existing.ItemData = incoming.ItemData;
            // Gewichte nur übernehmen, wenn die App sie kennt; sonst bleibt der gebuchte Verbrauch erhalten
            if (incoming.NetWeight is not null) existing.NetWeight = incoming.NetWeight;
            if (incoming.SpoolWeight is not null) existing.SpoolWeight = incoming.SpoolWeight;
            if (incoming.SpoolsInStock is not null) existing.SpoolsInStock = incoming.SpoolsInStock;
            // Ältere Apps kennen das Feld nicht und schicken es nie mit; ein Messwert bleibt dann erhalten
            if (incoming.TransmissionDistance is not null) existing.TransmissionDistance = incoming.TransmissionDistance;
            // Weitere Tags ebenso nur, wenn die App sie kennt; vergebene Tags werden übersprungen
            if (incoming.ExtraTags is not null)
            {
                var extra = new List<string>();
                foreach (var tag in incoming.ExtraTags.Select(t => t.Trim()).Distinct())
                {
                    if (tag.Length == 0 || tag == incoming.RfidTag) continue;
                    if ((itemByTag.TryGetValue(tag, out var tagOwner) && tagOwner != incoming.Id) || containerByTag.ContainsKey(tag))
                    {
                        skipped.Add(L.F("Filament '{0}': Tag {1} ist bereits vergeben.", incoming.Name, tag));
                        continue;
                    }
                    extra.Add(tag);
                }
                existing.ExtraTags = extra;
            }
            foreach (var stale in itemByTag.Where(e => e.Value == incoming.Id).Select(e => e.Key).ToList())
                itemByTag.Remove(stale);
            foreach (var tag in existing.AllTags) itemByTag[tag] = incoming.Id;
            items++;
        }
        await db.SaveChangesAsync();

        return new ImportResult(containers, items, skipped);
    }

    private static long ToMillis(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime FromMillis(long value) =>
        value > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime : DateTime.UtcNow;
}
