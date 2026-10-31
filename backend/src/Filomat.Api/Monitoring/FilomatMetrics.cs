using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Printers;
using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace Filomat.Api.Monitoring;

/// <summary>
/// Eigene Messwerte für den gemeinsamen Monitoring-Stack (Tonwerk-AI, deploy/monitoring/): Präfix <c>filomat_</c>, Zähler
/// auf <c>_total</c>, Labels nur mit wenigen festen Werten. Druckernamen sind Daten der Benutzer und kommen nie in ein
/// Label, nur der Druckertyp. Die Zähler laufen immer mit; zu sehen sind sie nur, wenn <c>Monitoring:Metrics</c> an ist.
/// </summary>
public static class FilomatMetrics
{
    public static readonly Counter PrinterSyncs = Metrics.CreateCounter(
        "filomat_printer_syncs_total", "Abgleiche mit Druckern nach Typ und Ergebnis (ok, unreachable, failed).", "type", "result");

    public static readonly Histogram PrinterSyncDuration = Metrics.CreateHistogram(
        "filomat_printer_sync_duration_seconds", "Dauer eines Abgleichs mit einem Drucker.",
        new HistogramConfiguration { LabelNames = ["type"], Buckets = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60] });

    public static readonly Counter PrintJobs = Metrics.CreateCounter(
        "filomat_print_jobs_total", "Neu gebuchte Drucke nach Druckertyp.", "type");

    public static readonly Counter FilamentUsedGrams = Metrics.CreateCounter(
        "filomat_filament_used_grams_total", "Gebuchter Filamentverbrauch in Gramm nach Druckertyp.", "type");

    public static readonly Counter Notifications = Metrics.CreateCounter(
        "filomat_notifications_total", "Meldungen über ntfy nach Ergebnis (ok, failed).", "result");

    public static readonly Counter BackupRuns = Metrics.CreateCounter(
        "filomat_backup_runs_total", "Nextcloud-Sicherungen nach Ergebnis (ok, failed).", "result");

    /// <summary>Ohne Anfangswert: Eine 0 hielte der Alarm „Sicherung zu alt“ für eine Sicherung von 1970.</summary>
    public static readonly Gauge BackupLastSuccess = Metrics.CreateGauge(
        "filomat_backup_last_success_timestamp_seconds", "Ende der letzten erfolgreichen Sicherung (Unix-Zeit).",
        new GaugeConfiguration { SuppressInitialValue = true });

    public static readonly Counter ApiKeyRejected = Metrics.CreateCounter(
        "filomat_api_key_rejected_total", "/api-Aufrufe ohne oder mit falschem X-Api-Key.");

    private static readonly Gauge Printers = Metrics.CreateGauge(
        "filomat_printers", "Drucker nach Typ und Zustand des letzten Abgleichs (reachable, unreachable, disabled).", "type", "state");

    private static readonly Gauge Spools = Metrics.CreateGauge(
        "filomat_spools", "Spulen nach Zustand: full (ungeöffnet im Lager), in_use, low (fast leer), empty.", "state");

    private static readonly Gauge Containers = Metrics.CreateGauge("filomat_containers", "Container.");
    private static readonly Gauge Items = Metrics.CreateGauge("filomat_items", "Filamentsorten.");

    public static string TypeLabel(PrinterType type) => type switch
    {
        PrinterType.Moonraker => "moonraker",
        PrinterType.SnapmakerU1 => "snapmaker_u1",
        PrinterType.PrusaLink => "prusalink",
        PrinterType.Anycubic => "anycubic",
        PrinterType.Bambu => "bambu",
        _ => "other",
    };

    /// <summary>Zählt den Bestand neu, direkt bevor /metrics antwortet; die Abfrage läuft im Scope dieser Anfrage.</summary>
    public static async Task RefreshAsync(FilomatDbContext db, CancellationToken cancellationToken)
    {
        var printers = await db.Printers.Select(p => new { p.Type, p.Enabled, p.LastSyncError }).ToListAsync(cancellationToken);
        foreach (var type in Enum.GetValues<PrinterType>())
        {
            var ofType = printers.Where(p => p.Type == type).ToList();
            Printers.WithLabels(TypeLabel(type), "reachable").Set(ofType.Count(p => p.Enabled && p.LastSyncError is null));
            Printers.WithLabels(TypeLabel(type), "unreachable").Set(ofType.Count(p => p.Enabled && p.LastSyncError is not null));
            Printers.WithLabels(TypeLabel(type), "disabled").Set(ofType.Count(p => !p.Enabled));
        }

        var spools = await db.Spools
            .Join(db.Items, s => s.ItemId, i => i.Id, (s, i) => new { s.EmptiedAt, s.RemainingWeight, i.NetWeight })
            .ToListAsync(cancellationToken);
        var open = spools.Where(s => s.EmptiedAt is null).ToList();
        var low = open.Count(s => s.RemainingWeight is { } remaining && SpoolLevel.IsLow(remaining, s.NetWeight));
        Spools.WithLabels("full").Set(await db.Items.SumAsync(i => i.SpoolsInStock ?? 0, cancellationToken));
        Spools.WithLabels("in_use").Set(open.Count - low);
        Spools.WithLabels("low").Set(low);
        Spools.WithLabels("empty").Set(spools.Count - open.Count);

        Containers.Set(await db.Containers.CountAsync(cancellationToken));
        Items.Set(await db.Items.CountAsync(cancellationToken));
    }
}
