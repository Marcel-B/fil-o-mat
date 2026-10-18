using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Printers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

/// <param name="RemainingWeight">Ohne Angabe das Füllgewicht der Sorte.</param>
public record OpenSpoolRequest(double? RemainingWeight = null, string? PrinterId = null, int? Tool = null);

/// <summary>Restgewicht und Platz einer Spule; <c>PrinterId = null</c> legt sie ins Lager.</summary>
public record SpoolUpdateRequest(double? RemainingWeight, string? PrinterId, int? Tool);

public static class SpoolEndpoints
{
    public static RouteGroupBuilder MapSpoolEndpoints(this RouteGroupBuilder api)
    {
        var spools = api.MapGroup("/spools").WithTags("Spulen");

        spools.MapGet("/", async (string? itemId, string? printerId, bool? includeEmpty, FilomatDbContext db) =>
        {
            var query = db.Spools.AsQueryable();
            if (includeEmpty != true) query = query.Where(s => s.EmptiedAt == null);
            if (itemId is not null) query = query.Where(s => s.ItemId == itemId);
            if (printerId is not null) query = query.Where(s => s.PrinterId == printerId);
            return await query.OrderBy(s => s.ItemId).ThenBy(s => s.Number).ToListAsync();
        });

        api.MapPost("/items/{id}/spools", async Task<Results<Created<Spool>, NotFound, ValidationProblem>> (
            string id, OpenSpoolRequest request, FilomatDbContext db, SpoolStock stock) =>
        {
            if (await db.Items.FindAsync(id) is not { } item) return TypedResults.NotFound();
            if (await ValidateAsync(db, request.RemainingWeight, request.PrinterId, request.Tool) is { } errors)
                return TypedResults.ValidationProblem(errors);
            var spool = await stock.OpenAsync(item, request.PrinterId, request.Tool, request.RemainingWeight);
            return TypedResults.Created($"/api/spools/{spool.Id}", spool);
        }).WithTags("Spulen").WithSummary("Neue Spule anbrechen (Vorrat minus eins)");

        spools.MapPut("/{id}", async Task<Results<Ok<Spool>, NotFound, ValidationProblem>> (
            string id, SpoolUpdateRequest request, FilomatDbContext db, SpoolStock stock) =>
        {
            if (await db.Spools.FindAsync(id) is not { } spool) return TypedResults.NotFound();
            if (await ValidateAsync(db, request.RemainingWeight, request.PrinterId, request.Tool) is { } errors)
                return TypedResults.ValidationProblem(errors);
            spool.RemainingWeight = request.RemainingWeight;
            await stock.PlaceAsync(spool, request.PrinterId, request.Tool);
            return TypedResults.Ok(spool);
        });

        spools.MapPost("/{id}/empty", async Task<Results<Ok<Spool>, NotFound>> (string id, FilomatDbContext db, SpoolStock stock) =>
        {
            if (await db.Spools.FindAsync(id) is not { } spool) return TypedResults.NotFound();
            await stock.MarkEmptyAsync(spool);
            return TypedResults.Ok(spool);
        }).WithSummary("Spule ist leer");

        return api;
    }

    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        FilomatDbContext db, double? remainingWeight, string? printerId, int? tool)
    {
        var errors = new Dictionary<string, string[]>();
        if (remainingWeight is < 0) errors["remainingWeight"] = ["Gewicht darf nicht negativ sein."];
        if (printerId is not null)
        {
            if (await db.Printers.FindAsync(printerId) is not { } printer) errors["printerId"] = ["Drucker nicht gefunden."];
            else if (tool is { } t && (t < 0 || t >= PrinterResponse.From(printer).Tools)) errors["tool"] = ["Diesen Kopf hat der Drucker nicht."];
        }
        return errors.Count > 0 ? errors : null;
    }
}
