using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

public record ItemRequest(
    string? Id,
    string RfidTag,
    string Name,
    string? Description,
    string? ContainerId,
    FilamentData? ItemData,
    double? NetWeight = null,
    double? SpoolWeight = null,
    int? SpoolsInStock = null,
    double? TransmissionDistance = null);

public record ItemUpdateRequest(
    string Name,
    string? Description,
    FilamentData? ItemData,
    double? NetWeight = null,
    double? SpoolWeight = null,
    int? SpoolsInStock = null,
    double? TransmissionDistance = null);

public record MoveItemRequest(string? ContainerId);

/// <param name="FromId">Filament, das in dieses übernommen und danach gelöscht wird.</param>
public record MergeItemRequest(string FromId);

/// <summary>Spule, die beim Zusammenführen eine neue Nummer bekommen hat (die alte gab es schon).</summary>
public record RenumberedSpool(int OldNumber, int NewNumber);

public record MergeItemResult(Item Item, List<RenumberedSpool> Renumbered);

public static class ItemEndpoints
{
    public static RouteGroupBuilder MapItemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/items").WithTags("Filamente");

        group.MapGet("/", async (string? containerId, FilomatDbContext db) =>
        {
            var query = db.Items.AsQueryable();
            if (containerId is not null) query = query.Where(i => i.ContainerId == containerId);
            return await query.OrderBy(i => i.CreatedAt).ToListAsync();
        });

        group.MapGet("/{id}", async Task<Results<Ok<Item>, NotFound>> (string id, FilomatDbContext db) =>
            await db.Items.FindAsync(id) is { } item ? TypedResults.Ok(item) : TypedResults.NotFound());

        group.MapGet("/by-tag/{rfidTag}", async Task<Results<Ok<Item>, NotFound>> (string rfidTag, FilomatDbContext db) =>
            await db.Items.WithTag(rfidTag).FirstOrDefaultAsync() is { } item
                ? TypedResults.Ok(item)
                : TypedResults.NotFound());

        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapPost("/{id}/move", MoveAsync);
        group.MapPost("/{id}/merge", MergeAsync).WithSummary("Doppelten Eintrag in dieses Filament zusammenführen");
        group.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (string id, FilomatDbContext db) =>
        {
            if (await db.Items.FindAsync(id) is not { } item) return TypedResults.NotFound();
            db.Items.Remove(item);
            await db.SaveChangesAsync();
            return TypedResults.NoContent();
        });

        return api;
    }

    private static async Task<Results<Created<Item>, ValidationProblem, Conflict<string>>> CreateAsync(
        ItemRequest request, FilomatDbContext db)
    {
        var tag = request.RfidTag?.Trim() ?? "";
        var name = request.Name?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = [L.T("Name fehlt.")];
        if (request.ContainerId is not null && !await db.Containers.AnyAsync(c => c.Id == request.ContainerId))
            errors["containerId"] = [L.T("Container nicht gefunden.")];
        if (request.TransmissionDistance is < 0) errors["transmissionDistance"] = [L.T("Die Lichtdurchlässigkeit darf nicht negativ sein.")];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
        if (await db.Items.AnyAsync(i => i.Id == id))
            return TypedResults.Conflict(L.F("Filament mit ID '{0}' existiert bereits.", id));
        if (tag.Length > 0)
        {
            if (await db.Items.WithTag(tag).AnyAsync())
                return TypedResults.Conflict(L.T("Dieses Tag gehört bereits zu einem Filament."));
            if (await db.Containers.FirstOrDefaultAsync(c => c.RfidTag == tag) is { } container)
                return TypedResults.Conflict(L.F("Dieses Tag gehört zum Container '{0}'.", container.Name));
        }

        var item = new Item
        {
            Id = id,
            RfidTag = tag,
            Name = name,
            Description = request.Description ?? "",
            ContainerId = request.ContainerId,
            ItemData = request.ItemData,
            NetWeight = request.NetWeight,
            SpoolWeight = request.SpoolWeight,
            SpoolsInStock = request.SpoolsInStock,
            TransmissionDistance = request.TransmissionDistance,
            CreatedAt = DateTime.UtcNow,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/items/{item.Id}", item);
    }

    private static async Task<Results<Ok<Item>, NotFound, ValidationProblem>> UpdateAsync(
        string id, ItemUpdateRequest request, FilomatDbContext db)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [L.T("Name fehlt.")] });
        if (request.TransmissionDistance is < 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transmissionDistance"] = [L.T("Die Lichtdurchlässigkeit darf nicht negativ sein.")],
            });
        if (await db.Items.FindAsync(id) is not { } item) return TypedResults.NotFound();

        item.Name = name;
        item.Description = request.Description ?? "";
        item.ItemData = request.ItemData;
        item.NetWeight = request.NetWeight;
        item.SpoolWeight = request.SpoolWeight;
        item.SpoolsInStock = request.SpoolsInStock;
        item.TransmissionDistance = request.TransmissionDistance;
        await db.SaveChangesAsync();
        return TypedResults.Ok(item);
    }

    /// <summary>
    /// Führt ein doppeltes Filament in dieses zusammen: Spulen, Verbrauch, Vorrat und Tags wandern hierher,
    /// fehlende Angaben werden ergänzt, danach wird das andere gelöscht. Spulennummern, die es hier schon
    /// gibt, bekommen die nächste freie Nummer.
    /// </summary>
    private static async Task<Results<Ok<MergeItemResult>, NotFound, ValidationProblem>> MergeAsync(
        string id, MergeItemRequest request, FilomatDbContext db)
    {
        if (request.FromId == id)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["fromId"] = [L.T("Ein Filament lässt sich nicht mit sich selbst zusammenführen.")],
            });
        if (await db.Items.FindAsync(id) is not { } target || await db.Items.FindAsync(request.FromId) is not { } source)
            return TypedResults.NotFound();

        var used = (await db.Spools.Where(s => s.ItemId == target.Id).Select(s => s.Number).ToListAsync()).ToHashSet();
        var moved = await db.Spools.Where(s => s.ItemId == source.Id).OrderBy(s => s.Number).ToListAsync();
        // Neue Nummern liegen hinter allen bisherigen, damit keine übernommene Spule eine zweite Nummer braucht
        var next = used.Concat(moved.Select(s => s.Number)).DefaultIfEmpty(0).Max() + 1;
        var renumbered = new List<RenumberedSpool>();
        foreach (var spool in moved)
        {
            if (used.Contains(spool.Number))
            {
                // Leere Spulen sind nur Verlauf, ihre Nummer steht auf keiner Spule mehr
                if (spool.EmptiedAt is null) renumbered.Add(new RenumberedSpool(spool.Number, next));
                spool.Number = next++;
            }
            spool.ItemId = target.Id;
        }
        foreach (var consumption in await db.Consumptions.Where(c => c.ItemId == source.Id).ToListAsync())
            consumption.ItemId = target.Id;

        if (target.SpoolsInStock is not null || source.SpoolsInStock is not null)
            target.SpoolsInStock = (target.SpoolsInStock ?? 0) + (source.SpoolsInStock ?? 0);
        target.ContainerId ??= source.ContainerId;
        target.ItemData ??= source.ItemData;
        target.NetWeight ??= source.NetWeight;
        target.SpoolWeight ??= source.SpoolWeight;
        target.TransmissionDistance ??= source.TransmissionDistance;
        if (target.RfidTag.Length == 0) target.RfidTag = source.RfidTag;
        target.ExtraTags = target.ExtraTags.Concat(source.AllTags)
            .Where(t => t.Length > 0 && t != target.RfidTag)
            .Distinct()
            .ToList();

        // Erst umhängen, dann löschen, damit die Löschweitergabe keine übernommene Spule erfasst
        await db.SaveChangesAsync();
        db.Items.Remove(source);
        await db.SaveChangesAsync();
        return TypedResults.Ok(new MergeItemResult(target, renumbered));
    }

    /// <summary>Lagert ein Filament in einen Container ein (<c>containerId</c>) oder nimmt es heraus (<c>null</c>).</summary>
    private static async Task<Results<Ok<Item>, NotFound, ValidationProblem>> MoveAsync(
        string id, MoveItemRequest request, FilomatDbContext db)
    {
        if (await db.Items.FindAsync(id) is not { } item) return TypedResults.NotFound();
        if (request.ContainerId is not null && !await db.Containers.AnyAsync(c => c.Id == request.ContainerId))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["containerId"] = [L.T("Container nicht gefunden.")],
            });

        item.ContainerId = request.ContainerId;
        await db.SaveChangesAsync();
        return TypedResults.Ok(item);
    }
}
