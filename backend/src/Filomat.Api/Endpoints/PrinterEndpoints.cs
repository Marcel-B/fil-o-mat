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
    int Tools,
    DateTime CreatedAt,
    DateTime? LastJobEndedAt,
    DateTime? LastSyncAt,
    string? LastSyncError,
    DateTime? ReaderSeenAt = null)
{
    public static PrinterResponse From(Printer p) => new(
        p.Id, p.Name, p.Type, p.BaseUrl, !string.IsNullOrEmpty(p.ApiKey), p.Enabled, p.ContainerId,
        p.ToolCount ?? (p.Type == PrinterType.SnapmakerU1 ? 4 : 1),
        p.CreatedAt, p.LastJobEndedAt, p.LastSyncAt, p.LastSyncError, p.ReaderSeenAt);
}

public record SyncResponse(int Recorded, string? Error);

/// <param name="SpoolId">Angebrochene Spule; ohne Angabe die Spule der Sorte in diesem Kopf bzw. die einzige angebrochene.</param>
public record AssignConsumptionRequest(string ItemId, string? SpoolId = null);

public static class PrinterEndpoints
{
    public static RouteGroupBuilder MapPrinterEndpoints(this RouteGroupBuilder api)
    {
        var printers = api.MapGroup("/printers").WithTags("Drucker");

        printers.MapGet("/", async (FilomatDbContext db) =>
            (await db.Printers.OrderBy(p => p.CreatedAt).ToListAsync()).Select(PrinterResponse.From));

        // Älter als das wird beim Drucker neu abgefragt; die Apps fragen etwa alle 15 s, solange sie offen sind
        var maxAge = TimeSpan.FromSeconds(10);

        printers.MapGet("/status", (PrinterLive live, CancellationToken cancellationToken) => live.GetAllAsync(maxAge, cancellationToken))
            .WithSummary("Zustand aller Drucker mit Spulen pro Kopf und Hinweisen");

        printers.MapGet("/{id}/status", async Task<Results<Ok<PrinterLiveResponse>, NotFound>> (
            string id, FilomatDbContext db, PrinterLive live, CancellationToken cancellationToken) =>
            await db.Printers.FindAsync([id], cancellationToken) is { } printer
                ? TypedResults.Ok(await live.GetAsync(printer, maxAge, cancellationToken))
                : TypedResults.NotFound());

        printers.MapGet("/{id}/thumbnail", async Task<Results<FileContentHttpResult, NotFound>> (
            string id, FilomatDbContext db, PrinterLive live, CancellationToken cancellationToken) =>
        {
            if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer) return TypedResults.NotFound();
            try
            {
                return await live.GetThumbnailAsync(printer, cancellationToken) is { } image
                    ? TypedResults.File(image.Data, image.ContentType)
                    : TypedResults.NotFound();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or PrinterUnavailableException or IOException && !cancellationToken.IsCancellationRequested)
            {
                return TypedResults.NotFound();
            }
        }).WithSummary("Vorschaubild des laufenden Drucks");

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

        printers.MapPut("/{id}/reader", async Task<Results<Ok<ReaderResponse>, NotFound, ValidationProblem>> (
            string id, ReaderReport report, FilomatDbContext db, PrinterReader reader, CancellationToken cancellationToken) =>
        {
            if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer) return TypedResults.NotFound();
            if (report.Slots is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["slots"] = ["Slots fehlen."] });
            return TypedResults.Ok(await reader.ReportAsync(printer, report.Slots, cancellationToken));
        }).WithSummary("NFC-Leser am Drucker meldet, welches Musterplättchen in welchem Slot steckt (ganzer Stand, ab Slot 0)");

        printers.MapPost("/{id}/reader/tap", async Task<Results<Ok<ReaderSlot>, NotFound, ValidationProblem>> (
            string id, ReaderTap tap, FilomatDbContext db, PrinterReader reader, CancellationToken cancellationToken) =>
        {
            if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer) return TypedResults.NotFound();
            var errors = new Dictionary<string, string[]>();
            if (PrinterReader.NormalizeUid(tap.Tag) is null) errors["tag"] = ["Tag fehlt."];
            if (tap.Slot < 0 || tap.Slot >= PrinterReader.MaxSlots) errors["slot"] = ["Slot ungültig."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            return TypedResults.Ok(await reader.TapAsync(printer, tap.Slot, tap.Tag, cancellationToken));
        }).WithSummary("Tipp-Modus: Plättchen am Leser angetippt und Slot gewählt, die Spule kommt in diesen Slot");

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
            string id, AssignConsumptionRequest request, FilomatDbContext db, SpoolStock spools) =>
        {
            if (await db.Consumptions.FindAsync(id) is not { } entry) return TypedResults.NotFound();
            if (entry.ItemId is not null) return TypedResults.Conflict("Dieser Verbrauch ist bereits einer Spule zugeordnet.");
            if (await db.Items.FindAsync(request.ItemId) is not { } item)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["itemId"] = ["Filament nicht gefunden."] });

            var open = await db.Spools.Where(s => s.ItemId == item.Id && s.EmptiedAt == null).ToListAsync();
            var spool = request.SpoolId is not null
                ? open.FirstOrDefault(s => s.Id == request.SpoolId)
                : open.FirstOrDefault(s => s.PrinterId == entry.PrinterId && s.Tool == entry.Tool)
                    ?? (open.Count == 1 ? open[0] : null);
            if (request.SpoolId is not null && spool is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["spoolId"] = ["Spule nicht gefunden."] });
            spool ??= await spools.OpenAsync(item);

            entry.ItemId = item.Id;
            await spools.DeductAsync(spool, item, entry.Grams);
            entry.SpoolId = spool.Id;
            await db.SaveChangesAsync();
            return TypedResults.Ok(entry);
        });

        return api;
    }

    private static async Task<Results<Created<PrinterResponse>, ValidationProblem, Conflict<string>>> CreateAsync(
        PrinterRequest request, FilomatDbContext db, ConsumptionSync sync, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            errors["baseUrl"] = ["Adresse muss mit http:// oder https:// beginnen."];
        if (request.Type == PrinterType.Bambu && string.IsNullOrWhiteSpace(request.ApiKey))
            errors["apiKey"] = ["Zugangscode fehlt (am Drucker unter Einstellungen > WLAN)."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var id = Guid.NewGuid().ToString();
        var tag = string.IsNullOrWhiteSpace(request.RfidTag) ? $"PRINTER_{id}" : request.RfidTag.Trim();
        if (await db.Containers.AnyAsync(c => c.RfidTag == tag) || await db.Items.WithTag(tag).AnyAsync())
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
        // Gleich abgleichen, damit eingelegte Spulen sofort im Container stehen; Fehler stehen danach am Drucker
        await sync.SyncAsync(printer, cancellationToken);
        return TypedResults.Created($"/api/printers/{printer.Id}", PrinterResponse.From(printer));
    }

    private static async Task<Results<Ok<PrinterResponse>, NotFound, ValidationProblem>> UpdateAsync(
        string id, PrinterUpdateRequest request, FilomatDbContext db, ConsumptionSync sync, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["Name fehlt."];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            errors["baseUrl"] = ["Adresse muss mit http:// oder https:// beginnen."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        if (await db.Printers.FindAsync(id) is not { } printer) return TypedResults.NotFound();

        var connectionChanged = printer.BaseUrl != baseUrl || request.ApiKey is not null;
        printer.Name = name;
        printer.BaseUrl = baseUrl;
        printer.Enabled = request.Enabled;
        if (request.ApiKey is not null)
            printer.ApiKey = request.ApiKey.Trim().Length == 0 ? null : request.ApiKey.Trim();
        if (await db.Containers.FindAsync(printer.ContainerId) is { } container) container.Name = name;
        await db.SaveChangesAsync();
        // Neue Adresse oder neuer Schlüssel (z. B. nach einer neuen IP per DHCP) gleich ausprobieren,
        // damit der alte Fehler nicht stehen bleibt
        if (connectionChanged && printer.Enabled) await sync.SyncAsync(printer, cancellationToken);
        return TypedResults.Ok(PrinterResponse.From(printer));
    }

    /// <summary>Entfernt den Drucker samt Container; eingelegte Spulen wandern nach "Lose", der Verbrauchsverlauf der Spulen bleibt.</summary>
    private static async Task<Results<NoContent, NotFound>> DeleteAsync(string id, FilomatDbContext db, PrinterStatusCache cache)
    {
        if (await db.Printers.FindAsync(id) is not { } printer) return TypedResults.NotFound();
        cache.Remove(printer.Id);

        var target = await db.Containers.AnyAsync(c => c.Id == Container.LooseId) ? Container.LooseId : null;
        await db.Items.Where(i => i.ContainerId == printer.ContainerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ContainerId, target));
        await db.Spools.Where(s => s.PrinterId == printer.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.PrinterId, (string?)null).SetProperty(s => s.Tool, (int?)null));
        db.Printers.Remove(printer);
        if (await db.Containers.FindAsync(printer.ContainerId) is { } container) db.Containers.Remove(container);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
