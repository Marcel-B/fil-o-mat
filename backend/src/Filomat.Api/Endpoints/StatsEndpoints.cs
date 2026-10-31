using Filomat.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Endpoints;

/// <param name="Jobs">Drucke (ein Druck mit mehreren Köpfen zählt einmal).</param>
/// <param name="PrintSeconds">Summe der Druckzeiten, soweit die Drucker sie melden.</param>
/// <param name="CancelledGrams">Verbrauch abgebrochener Drucke.</param>
public record StatsTotals(double Grams, int Jobs, int Completed, int Cancelled, long PrintSeconds, double CancelledGrams);

/// <param name="Month"><c>2026-10</c></param>
public record MonthStats(string Month, double Grams, int Jobs, long PrintSeconds);

public record PrinterStats(string PrinterId, string Name, StatsTotals Totals);

/// <param name="ItemId"><c>null</c> für Verbrauch ohne Zuordnung.</param>
public record ItemStats(string? ItemId, string Name, string? Type, string? ColorHex, double Grams, int Jobs);

public record TypeStats(string Type, double Grams);

/// <param name="From">Beginn des Zeitraums (Monatsanfang, UTC).</param>
public record StatsResponse(
    DateTime From,
    StatsTotals Totals,
    IReadOnlyList<MonthStats> Months,
    IReadOnlyList<PrinterStats> Printers,
    IReadOnlyList<ItemStats> Items,
    IReadOnlyList<TypeStats> Types);

public static class StatsEndpoints
{
    public static RouteGroupBuilder MapStatsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stats", async (int? months, FilomatDbContext db, CancellationToken cancellationToken) =>
        {
            var count = Math.Clamp(months ?? 12, 1, 120);
            var now = DateTime.UtcNow;
            var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(count - 1));

            var entries = await db.Consumptions.Where(c => c.EndedAt >= from).ToListAsync(cancellationToken);
            var printers = await db.Printers.ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            var itemIds = entries.Select(c => c.ItemId).OfType<string>().Distinct().ToList();
            var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

            var monthList = Enumerable.Range(0, count).Select(i => from.AddMonths(i)).Select(month =>
            {
                var inMonth = entries.Where(c => c.EndedAt.Year == month.Year && c.EndedAt.Month == month.Month).ToList();
                var totals = Totals(inMonth);
                return new MonthStats($"{month:yyyy-MM}", totals.Grams, totals.Jobs, totals.PrintSeconds);
            }).ToList();

            var perPrinter = entries.GroupBy(c => c.PrinterId)
                .Select(g => new PrinterStats(g.Key, printers.GetValueOrDefault(g.Key, L.T("Gelöschter Drucker")), Totals(g.ToList())))
                .OrderByDescending(p => p.Totals.Grams)
                .ToList();

            var perItem = entries.GroupBy(c => c.ItemId)
                .Select(g =>
                {
                    var item = g.Key is null ? null : items.GetValueOrDefault(g.Key);
                    return new ItemStats(g.Key, item?.Name ?? L.T("Ohne Zuordnung"), item?.ItemData?.Type, item?.ItemData?.ColorHex,
                        Math.Round(g.Sum(c => c.Grams), 1), g.Select(c => (c.PrinterId, c.JobId)).Distinct().Count());
                })
                .OrderByDescending(i => i.Grams)
                .ToList();

            var perType = entries.GroupBy(c => c.ItemId is not null && items.GetValueOrDefault(c.ItemId)?.ItemData?.Type is { Length: > 0 } type
                    ? type.Trim()
                    : "Unbekannt")
                .Select(g => new TypeStats(g.Key, Math.Round(g.Sum(c => c.Grams), 1)))
                .OrderByDescending(t => t.Grams)
                .ToList();

            return new StatsResponse(from, Totals(entries), monthList, perPrinter, perItem, perType);
        }).WithTags("Statistik").WithSummary("Verbrauch, Drucke und Druckzeit pro Monat, Drucker und Sorte");

        return api;
    }

    /// <summary>Ein Druck hat pro Kopf eine Zeile; Status und Druckzeit zählen pro Druck.</summary>
    private static StatsTotals Totals(IReadOnlyList<Domain.Consumption> entries)
    {
        var jobs = entries.GroupBy(c => (c.PrinterId, c.JobId)).Select(g => (
            g.First().Status,
            Seconds: g.Max(c => c.DurationSeconds) ?? 0,
            Grams: g.Sum(c => c.Grams))).ToList();
        return new StatsTotals(
            Math.Round(entries.Sum(c => c.Grams), 1),
            jobs.Count,
            jobs.Count(j => j.Status == "completed"),
            jobs.Count(j => j.Status != "completed"),
            jobs.Sum(j => (long)j.Seconds),
            Math.Round(jobs.Where(j => j.Status != "completed").Sum(j => j.Grams), 1));
    }
}
