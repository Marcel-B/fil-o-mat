using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Printers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

public record PrinterRequest(string Name, PrinterType Type, string BaseUrl, string? ApiKey, string? RfidTag);

/// <param name="ApiKey"><c>null</c> lässt den gespeicherten Schlüssel unverändert, ein leerer Text löscht ihn.</param>
public record PrinterUpdateRequest(string Name, string BaseUrl, string? ApiKey, bool Enabled);

/// <summary>Drucker ohne gespeicherten Schlüssel.</summary>
public record PrinterResponse(
    string Id,
    string Name,
    PrinterType Type,
    string BaseUrl,
    bool HasApiKey,
    bool Enabled,
    string ContainerId,
    DateTime CreatedAt,
    DateTime? LastJobEndedAt,
    DateTime? LastSyncAt,
    string? LastSyncError)
{
    public static PrinterResponse From(Printer p) => new(
        p.Id, p.Name, p.Type, p.BaseUrl, !string.IsNullOrEmpty(p.ApiKey), p.Enabled, p.ContainerId,
        p.CreatedAt, p.LastJobEndedAt, p.LastSyncAt, p.LastSyncError);
}

public record SyncResponse(int Recorded, string? Error);

public record AssignConsumptionRequest(string ItemId);

public static class PrinterEndpoints
{
    public static RouteGroupBuilder MapPrinterEndpoints(this RouteGroupBuilder api)
    {
        var printers = api.MapGroup("/printers").WithTags("Drucker");

        printers.MapGet("/", async (FilomatDbContext db) =>
            (await db.Printers.OrderBy(p => p.CreatedAt).ToListAsync()).Select(PrinterResponse.From));

        printers.MapGet("/{id}", async Task<Results<Ok<PrinterResponse>, NotFound>> (string id, FilomatDbContext db) =>
            await db.Printers.FindAsync(id) is { } printer
                ? TypedResults.Ok(PrinterResponse.From(printer))
                : TypedResults.NotFound());

        printers.MapPost("/", CreateAsync);
        printers.MapPut("/{id}", UpdateAsync);
        printers.MapDelete("/{id}", DeleteAsync);

        printers.MapPost("/{id}/sync", async Task<Results<Ok<SyncResponse>, NotFound>> (
            string id, FilomatDbContext db, ConsumptionSync sync, CancellationToken cancellationToken) =>
        {
            if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer) return TypedResults.NotFound();
            var recorded = await sync.SyncAsync(printer, cancellationToken);
            return TypedResults.Ok(new SyncResponse(recorded, printer.LastSyncError));
        });

        var consumption = api.MapGroup("/consumption").WithTags("Verbrauch");

        consumption.MapGet("/", async (string? itemId, string? printerId, bool? unassigned, FilomatDbContext db) =>
        {
            var query = db.Consumptions.AsQueryable();
            if (itemId is not null) query = query.Where(c => c.ItemId == itemId);
            if (printerId is not null) query = query.Where(c => c.PrinterId == printerId);
            if (unassigned == true) query = query.Where(c => c.ItemId == null);
            return await query.OrderByDescending(c => c.EndedAt).ToListAsync();
        });

        consumption.MapPost("/{id}/assign", async Task<Results<Ok<Consumption>, NotFound, ValidationProblem, Conflict<string>>> (
            string id, AssignConsumptionRequest request, FilomatDbContext db) =>
        {
            if (await db.Consumptions.FindAsync(id) is not { } entry) return TypedResults.NotFound();
            if (entry.ItemId is not null) return TypedResults.Conflict("Dieser Verbrauch ist bereits einer Spule zugeordnet.");
            if (await db.Items.FindAsync(request.ItemId) is not { } item)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["itemId"] = ["Filament nicht gefunden."] });

            entry.ItemId = item.Id;
            ConsumptionSync.Deduct(item, entry.Grams);
            await db.SaveChangesAsync();
            return TypedResults.Ok(entry);
        });

        return api;
    }

    private static async Task<Results<Created<PrinterResponse>, ValidationProblem, Conflict<string>>> CreateAsync(
        PrinterRequest request, FilomatDbContext db)
    {
        var name = request.Name?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            errors["baseUrl"] = ["Adresse muss mit http:// oder https:// beginnen."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var id = Guid.NewGuid().ToString();
        var tag = string.IsNullOrWhiteSpace(request.RfidTag) ? $"PRINTER_{id}" : request.RfidTag.Trim();
        if (await db.Containers.AnyAsync(c => c.RfidTag == tag) || await db.Items.AnyAsync(i => i.RfidTag == tag))
            return TypedResults.Conflict("Dieses Tag ist bereits vergeben.");

        var now = DateTime.UtcNow;
        var container = new Container
        {
            Id = Guid.NewGuid().ToString(),
            RfidTag = tag,
            Name = name,
            Description = "Spulen im Drucker",
            CreatedAt = now,
        };
        var printer = new Printer
        {
            Id = id,
            Name = name,
            Type = request.Type,
            BaseUrl = baseUrl,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim(),
            ContainerId = container.Id,
            CreatedAt = now,
        };
        db.Containers.Add(container);
        db.Printers.Add(printer);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/printers/{printer.Id}", PrinterResponse.From(printer));
    }

    private static async Task<Results<Ok<PrinterResponse>, NotFound, ValidationProblem>> UpdateAsync(
        string id, PrinterUpdateRequest request, FilomatDbContext db)
    {
        var name = request.Name?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            errors["baseUrl"] = ["Adresse muss mit http:// oder https:// beginnen."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        if (await db.Printers.FindAsync(id) is not { } printer) return TypedResults.NotFound();

        printer.Name = name;
        printer.BaseUrl = baseUrl;
        printer.Enabled = request.Enabled;
        if (request.ApiKey is not null)
            printer.ApiKey = request.ApiKey.Trim().Length == 0 ? null : request.ApiKey.Trim();
        if (await db.Containers.FindAsync(printer.ContainerId) is { } container) container.Name = name;
        await db.SaveChangesAsync();
        return TypedResults.Ok(PrinterResponse.From(printer));
    }

    /// <summary>Entfernt den Drucker samt Container; eingelegte Spulen wandern nach "Lose", der Verbrauchsverlauf der Spulen bleibt.</summary>
    private static async Task<Results<NoContent, NotFound>> DeleteAsync(string id, FilomatDbContext db)
    {
        if (await db.Printers.FindAsync(id) is not { } printer) return TypedResults.NotFound();

        var target = await db.Containers.AnyAsync(c => c.Id == Container.LooseId) ? Container.LooseId : null;
        await db.Items.Where(i => i.ContainerId == printer.ContainerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ContainerId, target));
        db.Printers.Remove(printer);
        if (await db.Containers.FindAsync(printer.ContainerId) is { } container) db.Containers.Remove(container);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
