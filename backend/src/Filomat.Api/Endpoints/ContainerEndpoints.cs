using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

public record ContainerRequest(string? Id, string RfidTag, string Name, string? Description, int? Capacity);

public record ContainerUpdateRequest(string Name, string? Description, int? Capacity);

public static class ContainerEndpoints
{
    public static RouteGroupBuilder MapContainerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/containers").WithTags("Container");

        group.MapGet("/", async (FilomatDbContext db) =>
            await db.Containers
                .OrderBy(c => c.IsStandard)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync());

        group.MapGet("/{id}", async Task<Results<Ok<Container>, NotFound>> (string id, FilomatDbContext db) =>
            await db.Containers.FindAsync(id) is { } container ? TypedResults.Ok(container) : TypedResults.NotFound());

        group.MapGet("/by-tag/{rfidTag}", async Task<Results<Ok<Container>, NotFound>> (string rfidTag, FilomatDbContext db) =>
            await db.Containers.FirstOrDefaultAsync(c => c.RfidTag == rfidTag) is { } container
                ? TypedResults.Ok(container)
                : TypedResults.NotFound());

        group.MapGet("/{id}/items", async Task<Results<Ok<List<Item>>, NotFound>> (string id, FilomatDbContext db) =>
        {
            if (!await db.Containers.AnyAsync(c => c.Id == id)) return TypedResults.NotFound();
            return TypedResults.Ok(await db.Items.Where(i => i.ContainerId == id).OrderBy(i => i.CreatedAt).ToListAsync());
        });

        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapDelete("/{id}", DeleteAsync);

        return api;
    }

    private static async Task<Results<Created<Container>, ValidationProblem, Conflict<string>>> CreateAsync(
        ContainerRequest request, FilomatDbContext db)
    {
        var tag = request.RfidTag?.Trim() ?? "";
        var name = request.Name?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (tag.Length == 0) errors["rfidTag"] = ["RFID-Tag fehlt."];
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
        if (await db.Containers.AnyAsync(c => c.Id == id))
            return TypedResults.Conflict($"Container mit ID '{id}' existiert bereits.");
        if (await db.Containers.FirstOrDefaultAsync(c => c.RfidTag == tag) is { } other)
            return TypedResults.Conflict($"Dieses Tag gehört bereits zum Container '{other.Name}'.");
        if (await db.Items.AnyAsync(i => i.RfidTag == tag))
            return TypedResults.Conflict("Dieses Tag gehört bereits zu einem Filament.");

        var container = new Container
        {
            Id = id,
            RfidTag = tag,
            Name = name,
            Description = request.Description ?? "",
            Capacity = request.Capacity,
            CreatedAt = DateTime.UtcNow,
        };
        db.Containers.Add(container);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/containers/{container.Id}", container);
    }

    private static async Task<Results<Ok<Container>, NotFound, ValidationProblem>> UpdateAsync(
        string id, ContainerUpdateRequest request, FilomatDbContext db)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name fehlt."] });
        if (await db.Containers.FindAsync(id) is not { } container) return TypedResults.NotFound();

        container.Name = name;
        container.Description = request.Description ?? "";
        container.Capacity = request.Capacity;
        await db.SaveChangesAsync();
        return TypedResults.Ok(container);
    }

    /// <summary>Löscht einen Container; enthaltene Filamente wandern in den Container "Lose".</summary>
    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> DeleteAsync(string id, FilomatDbContext db)
    {
        if (await db.Containers.FindAsync(id) is not { } container) return TypedResults.NotFound();
        if (container.IsStandard) return TypedResults.BadRequest("Standard-Container können nicht gelöscht werden.");
        if (await db.Printers.FirstOrDefaultAsync(p => p.ContainerId == id) is { } printer)
            return TypedResults.BadRequest($"Der Container gehört zum Drucker '{printer.Name}'. Bitte den Drucker entfernen.");

        var target = await db.Containers.AnyAsync(c => c.Id == Container.LooseId) ? Container.LooseId : null;
        await db.Items.Where(i => i.ContainerId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ContainerId, target));
        db.Containers.Remove(container);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
