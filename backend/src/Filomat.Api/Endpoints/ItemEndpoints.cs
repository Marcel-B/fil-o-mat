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
            await db.Items.FirstOrDefaultAsync(i => i.RfidTag == rfidTag) is { } item
                ? TypedResults.Ok(item)
                : TypedResults.NotFound());

        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapPost("/{id}/move", MoveAsync);
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
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (request.ContainerId is not null && !await db.Containers.AnyAsync(c => c.Id == request.ContainerId))
            errors["containerId"] = ["Container nicht gefunden."];
        if (request.TransmissionDistance is < 0) errors["transmissionDistance"] = ["Die Lichtdurchlässigkeit darf nicht negativ sein."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
        if (await db.Items.AnyAsync(i => i.Id == id))
            return TypedResults.Conflict($"Filament mit ID '{id}' existiert bereits.");
        if (tag.Length > 0)
        {
            if (await db.Items.AnyAsync(i => i.RfidTag == tag))
                return TypedResults.Conflict("Dieses Tag gehört bereits zu einem Filament.");
            if (await db.Containers.FirstOrDefaultAsync(c => c.RfidTag == tag) is { } container)
                return TypedResults.Conflict($"Dieses Tag gehört zum Container '{container.Name}'.");
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
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name fehlt."] });
        if (request.TransmissionDistance is < 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transmissionDistance"] = ["Die Lichtdurchlässigkeit darf nicht negativ sein."],
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

    /// <summary>Lagert ein Filament in einen Container ein (<c>containerId</c>) oder nimmt es heraus (<c>null</c>).</summary>
    private static async Task<Results<Ok<Item>, NotFound, ValidationProblem>> MoveAsync(
        string id, MoveItemRequest request, FilomatDbContext db)
    {
        if (await db.Items.FindAsync(id) is not { } item) return TypedResults.NotFound();
        if (request.ContainerId is not null && !await db.Containers.AnyAsync(c => c.Id == request.ContainerId))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["containerId"] = ["Container nicht gefunden."],
            });

        item.ContainerId = request.ContainerId;
        await db.SaveChangesAsync();
        return TypedResults.Ok(item);
    }
}
