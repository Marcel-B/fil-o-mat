using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Anycubic Kobra X, S1, 3 und 4 im LAN-Modus. Der Drucker kennt keine Druckhistorie, meldet aber die tatsächlich
/// geförderte Länge des laufenden Drucks (<c>supplies_usage</c>, mm). Der Adapter merkt sich den Druck
/// (<see cref="Printer.AdapterStateJson"/>) und rechnet jeden Zuwachs zwischen zwei Abgleichen dem ACE-Slot zu, der
/// gerade fördert; ohne Slot-Angabe wird der Rest anteilig verteilt. Slots zählen über alle ACE-Einheiten
/// (Einheit · 4 + Slot).
/// </summary>
public class AnycubicAdapter(IAnycubicLan lan) : IPrinterAdapter
{
    public const int SlotsPerBox = 4;

    /// <summary>Zuwachs ohne bekannten Slot.</summary>
    private const int UnknownSlot = -1;

    public PrinterType Type => PrinterType.Anycubic;

    /// <summary>Laufender Druck, wie beim letzten Abgleich gesehen.</summary>
    /// <param name="UsageMm">Zuletzt gemeldete Länge.</param>
    /// <param name="Slot">Slot, der beim letzten Abgleich förderte.</param>
    /// <param name="MmPerSlot">Bisher pro Slot zugerechnete Länge.</param>
    /// <param name="SlotTypes">Material pro Slot für die Umrechnung in Gramm.</param>
    /// <param name="Provisional">Der Drucker hat noch keine Auftragsnummer gemeldet; <see cref="JobId"/> ist selbst vergeben.</param>
    /// <param name="State">Zuletzt gemeldeter Zustand des Drucks.</param>
    public record TrackedJob(
        string JobId,
        string Name,
        double Progress,
        double UsageMm,
        int? Slot,
        Dictionary<int, double> MmPerSlot,
        Dictionary<int, string> SlotTypes,
        bool Provisional = false,
        string? State = null,
        double? PrintMinutes = null);

    /// <summary>Meldung aus dem letzten Abgleich pro Drucker, damit der Zustand danach keine zweite MQTT-Abfrage braucht.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AnycubicReport> _reports = new();

    public async Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(Printer printer, DateTime since, CancellationToken cancellationToken)
    {
        var report = await lan.QueryAsync(printer.BaseUrl, cancellationToken);
        _reports[printer.Id] = report;
        var ace = ReadAce(report.Boxes);
        printer.ToolCount = ace.SlotCount > 0 ? ace.SlotCount : 1;

        var tracked = ReadTracked(printer);
        var finished = new List<PrintJob>();
        report.Info.TryGetProperty("project", out var project);

        if (project.ValueKind != JsonValueKind.Object)
        {
            // Kein Druck mehr: der gemerkte ist beendet. last_project hat die endgültige Länge.
            if (tracked is not null)
            {
                report.Info.TryGetProperty("last_project", out var last);
                var status = (string?)null;
                if (last.ValueKind == JsonValueKind.Object && (JobIdOf(last) == tracked.JobId || tracked.Provisional && JobIdOf(last) is null))
                {
                    tracked = Advance(tracked, last, ace);
                    status = StatusOf(last);
                }
                finished.Add(Finish(printer, tracked, status ?? Ended(tracked)));
            }
            printer.AdapterStateJson = null;
            return finished;
        }

        // Beim Aufheizen meldet der Kobra X task_id -1; bis eine echte Nummer kommt, gilt eine eigene
        var jobId = JobIdOf(project);
        if (jobId is null)
        {
            if (tracked is { Provisional: false })
            {
                finished.Add(Finish(printer, tracked, Ended(tracked)));
                tracked = null;
            }
            tracked ??= new TrackedJob($"local-{DateTime.UtcNow:yyyyMMddHHmmss}", NameOf(project), 0, 0, null, [], [], Provisional: true);
        }
        else if (tracked is { Provisional: true })
        {
            tracked = tracked with { JobId = jobId, Provisional = false };
        }
        else if (tracked is not null && tracked.JobId != jobId)
        {
            finished.Add(Finish(printer, tracked, Ended(tracked)));
            tracked = null;
        }

        var current = Advance(tracked ?? new TrackedJob(jobId!, NameOf(project), 0, 0, null, [], []), project, ace);
        if (current.Name.Length == 0) current = current with { Name = NameOf(project) };
        if (StatusOf(project) is { } done)
        {
            finished.Add(Finish(printer, current, done));
            printer.AdapterStateJson = null;
        }
        else
        {
            printer.AdapterStateJson = JsonSerializer.Serialize(current);
        }
        return finished;
    }

    public async Task<PrinterStatus?> GetStatusAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (!_reports.TryRemove(printer.Id, out var report)) report = await lan.QueryAsync(printer.BaseUrl, cancellationToken);
        var info = report.Info;
        var (slots, dryers) = ReadSlots(report.Boxes);
        var ace = ReadAce(report.Boxes);
        info.TryGetProperty("project", out var project);
        var heaters = ReadHeaters(info, project);

        if (project.ValueKind != JsonValueKind.Object)
            return new PrinterStatus(PrintState.Idle, Heaters: heaters, Slots: slots, Dryers: dryers);

        var state = project.GetStringOrNull("state");
        var printState = StatusOf(project) switch
        {
            "completed" => PrintState.Finished,
            "cancelled" => PrintState.Cancelled,
            _ when project.GetDoubleOrNull("pause") is > 0 || state is "paused" or "pausing" => PrintState.Paused,
            _ when state is "failed" or "error" => PrintState.Error,
            _ => PrintState.Printing,
        };
        var progress = Math.Clamp(project.GetDoubleOrNull("progress") ?? 0, 0, 100);
        var tracked = ReadTracked(printer);
        var name = NameOf(project);

        return new PrinterStatus(
            printState,
            JobName: name.Length > 0 ? name : tracked?.Name,
            JobKey: JobIdOf(project) ?? tracked?.JobId,
            Progress: printState == PrintState.Finished ? 100 : progress,
            // Minuten
            RemainingSeconds: project.GetDoubleOrNull("remain_time") is { } remain ? (int)(remain * 60) : null,
            ElapsedSeconds: project.GetDoubleOrNull("print_time") is { } elapsed ? (int)(elapsed * 60) : null,
            Layer: (int?)project.GetDoubleOrNull("curr_layer"),
            TotalLayers: (int?)project.GetDoubleOrNull("total_layers"),
            ActiveTool: ace.LoadedSlot,
            Heaters: heaters,
            Slots: slots,
            Dryers: dryers,
            JobGramsPerTool: tracked is null ? null : EstimateGrams(tracked, progress, ace.SlotCount),
            JobGramsEstimated: true);
    }

    /// <summary>
    /// Der Drucker kennt das Slicer-Gewicht nicht. Hochgerechnet aus der bisher geförderten Länge und dem Fortschritt,
    /// verteilt wie bisher auf die Slots; erst ab 3 % einigermaßen verlässlich.
    /// </summary>
    internal static IReadOnlyList<double>? EstimateGrams(TrackedJob job, double progress, int slotCount)
    {
        if (progress < 3 || job.UsageMm <= 0) return null;
        var scale = 100 / progress;
        var perSlot = new double[Math.Max(slotCount, 1)];
        var known = job.MmPerSlot.Where(p => p.Key >= 0 && p.Key < perSlot.Length && p.Value > 0).ToDictionary();
        var knownSum = known.Values.Sum();
        if (knownSum <= 0)
        {
            perSlot[Math.Clamp(job.Slot ?? 0, 0, perSlot.Length - 1)] = FilamentMath.GramsFromLength(job.UsageMm * scale, null);
            return perSlot;
        }
        foreach (var (slot, mm) in known)
            perSlot[slot] = FilamentMath.GramsFromLength(job.UsageMm * mm / knownSum * scale, job.SlotTypes.GetValueOrDefault(slot));
        return perSlot;
    }

    /// <summary>Temperaturen; je nach Firmware in <c>temp</c> der Info-Meldung oder im Projekt.</summary>
    private static List<Heater> ReadHeaters(JsonElement info, JsonElement project)
    {
        JsonElement source = default;
        foreach (var candidate in new[] { info, project })
        {
            if (candidate.ValueKind != JsonValueKind.Object) continue;
            if (candidate.TryGetProperty("temp", out var temp) && temp.ValueKind == JsonValueKind.Object) { source = temp; break; }
            if (candidate.GetDoubleOrNull("curr_nozzle_temp") is not null) { source = candidate; break; }
        }
        var heaters = new List<Heater>();
        if (source.ValueKind != JsonValueKind.Object) return heaters;
        if (source.GetDoubleOrNull("curr_nozzle_temp") is { } nozzle)
            heaters.Add(new Heater("Düse", 0, nozzle, source.GetDoubleOrNull("target_nozzle_temp")));
        if (source.GetDoubleOrNull("curr_hotbed_temp") is { } bed)
            heaters.Add(new Heater("Bett", null, bed, source.GetDoubleOrNull("target_hotbed_temp")));
        return heaters;
    }

    /// <summary>Slots mit Material und Farbe sowie Feuchte pro Einheit; Zählung wie <see cref="ReadAce"/>.</summary>
    public static (List<SlotReport> Slots, List<DryerReport> Dryers) ReadSlots(JsonElement? boxes)
    {
        var slots = new List<SlotReport>();
        var dryers = new List<DryerReport>();
        if (boxes is not { ValueKind: JsonValueKind.Array } list) return (slots, dryers);
        var all = list.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object).ToList();
        if (all.Count == 0) return (slots, dryers);
        var ids = all.Select(b => (int)(b.GetDoubleOrNull("id") ?? 0)).ToList();
        var offset = -Math.Min(0, ids.Min());

        for (var b = 0; b < all.Count; b++)
        {
            var box = all[b];
            var first = (ids[b] + offset) * SlotsPerBox;
            var tools = new List<int>();
            if (box.TryGetProperty("slots", out var boxSlots) && boxSlots.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in boxSlots.EnumerateArray())
                {
                    if (s.GetDoubleOrNull("edit_status") == LinkedSlot) continue;
                    var index = first + (int)(s.GetDoubleOrNull("index") ?? 0);
                    tools.Add(index);
                    var type = s.GetStringOrNull("type") is { Length: > 0 } t ? t : null;
                    var status = s.GetDoubleOrNull("status");
                    bool? loaded = type is null || status == 0 ? false : status is 4 or 5 ? true : null;
                    slots.Add(new SlotReport(index, loaded, type, loaded == false ? null : ColorOf(s)));
                }
            }
            var humidity = box.GetDoubleOrNull("humidity");
            var temperature = box.GetDoubleOrNull("temp");
            if (humidity is > 0 || temperature is > 0)
                dryers.Add(new DryerReport(ids[b] < 0 ? "Rack" : $"ACE {ids[b] + 1}", humidity is > 0 ? humidity : null, null, temperature, tools));
        }
        return (slots.OrderBy(s => s.Tool).ToList(), dryers);
    }

    private static string? ColorOf(JsonElement slot)
    {
        if (!slot.TryGetProperty("color", out var color) || color.ValueKind != JsonValueKind.Array) return null;
        var rgb = color.EnumerateArray().Select(c => c.TryGetInt32(out var v) ? Math.Clamp(v, 0, 255) : 0).ToList();
        return rgb.Count >= 3 ? $"{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}" : null;
    }

    /// <summary>Rechnet den Zuwachs seit dem letzten Abgleich dem Slot zu, der beim letzten Abgleich förderte.</summary>
    private static TrackedJob Advance(TrackedJob job, JsonElement project, AceState ace)
    {
        var usage = project.GetDoubleOrNull("supplies_usage") ?? job.UsageMm;
        var delta = usage - job.UsageMm;
        var perSlot = new Dictionary<int, double>(job.MmPerSlot);
        if (delta > 0)
        {
            // Beim ersten Blick auf den Druck zählt der Slot, der jetzt fördert
            var slot = job.Slot ?? ace.LoadedSlot ?? UnknownSlot;
            perSlot[slot] = perSlot.GetValueOrDefault(slot) + delta;
        }

        var types = new Dictionary<int, string>(job.SlotTypes);
        foreach (var (slot, type) in ace.Types) types[slot] = type;

        return job with
        {
            State = project.GetStringOrNull("state") ?? job.State,
            PrintMinutes = project.GetDoubleOrNull("print_time") ?? job.PrintMinutes,
            Progress = Math.Max(job.Progress, project.GetDoubleOrNull("progress") ?? 0),
            UsageMm = Math.Max(usage, job.UsageMm),
            Slot = ace.LoadedSlot ?? job.Slot,
            MmPerSlot = perSlot,
            SlotTypes = types,
        };
    }

    private static PrintJob Finish(Printer printer, TrackedJob job, string status)
    {
        var known = job.MmPerSlot.Where(p => p.Key >= 0 && p.Value > 0).ToDictionary();
        var unknown = job.MmPerSlot.GetValueOrDefault(UnknownSlot);
        var total = known.Values.Sum() + unknown;
        string? type = known.Count == 1 ? job.SlotTypes.GetValueOrDefault(known.Keys.Single()) : null;

        // Ohne ACE oder ohne jeden bekannten Slot: alles auf Kopf 0
        var slots = printer.ToolCount ?? 1;
        if (slots <= 1 || known.Count == 0 || known.Keys.Max() >= slots)
            return new PrintJob(job.JobId, job.Name, status, DateTime.UtcNow, total, FilamentType: type, DurationSeconds: Duration(job));

        // Länge ohne Slot anteilig auf die bekannten Slots
        var knownSum = known.Values.Sum();
        var lengths = new double[slots];
        var grams = new double[slots];
        foreach (var (slot, mm) in known)
        {
            lengths[slot] = mm + unknown * mm / knownSum;
            grams[slot] = FilamentMath.GramsFromLength(lengths[slot], job.SlotTypes.GetValueOrDefault(slot));
        }
        return new PrintJob(
            JobId: job.JobId,
            Name: job.Name,
            Status: status,
            EndedAt: DateTime.UtcNow,
            LengthMm: total,
            SlicedLengthMm: total,
            SlicedGrams: grams.Sum(),
            FilamentType: type,
            SlicedGramsPerFilament: grams,
            SlicedLengthPerFilament: lengths,
            DurationSeconds: Duration(job));
    }

    private static int? Duration(TrackedJob job) => job.PrintMinutes is double minutes and > 0 ? (int)(minutes * 60) : null;

    /// <summary>
    /// <c>print_status</c> 2 = fertig, 3 = abgebrochen; alles andere (auch die 0 des Kobra X) läuft noch.
    /// <c>last_project</c> des Kobra X hat kein <c>print_status</c>, nur <c>state</c>.
    /// </summary>
    private static string? StatusOf(JsonElement project) => project.GetDoubleOrNull("print_status") switch
    {
        2 => "completed",
        3 => "cancelled",
        _ => project.GetStringOrNull("state") switch
        {
            "finished" => "completed",
            "canceled" or "cancelled" or "stopped" => "cancelled",
            _ => null,
        },
    };

    /// <summary>
    /// Ein Druck, der ohne Abschlussmeldung verschwindet: fertig, wenn er fast durch war oder schon abkühlte. Der
    /// Kobra X bleibt am Ende bei 99 % und meldet danach in <c>last_project</c> noch den Druck davor.
    /// </summary>
    private static string Ended(TrackedJob job) =>
        job.Progress >= 99 || job.State is "nozzle_cooling_off" or "finished" ? "completed" : "cancelled";

    /// <summary>Auftragsnummer des Druckers; 0 und -1 (Kobra X beim Aufheizen) zählen nicht.</summary>
    private static string? JobIdOf(JsonElement project)
    {
        if (project.GetDoubleOrNull("task_id") is > 0) return project.GetStringOrNull("task_id");
        return project.GetStringOrNull("localtask") is { Length: > 0 } local ? local : null;
    }

    private static string NameOf(JsonElement project) =>
        Path.GetFileNameWithoutExtension(project.GetStringOrNull("filename") ?? "");

    /// <summary>Slots der ACE-Einheiten: Anzahl, förderndem Slot und Material.</summary>
    public record AceState(int SlotCount, int? LoadedSlot, Dictionary<int, string> Types);

    /// <summary><c>edit_status</c> eines Slots, der nur zu einer weiteren ACE durchreicht (Kobra X).</summary>
    private const int LinkedSlot = 3;

    /// <summary>
    /// Slots über alle Einheiten: Einheit · 4 + Slot. Der Kobra X meldet seinen eingebauten Vierer als Einheit -1;
    /// dann zählen alle Einheiten um eins verschoben (eingebaut 0–3, erste externe ACE 4–7). Ein eingebauter
    /// Slot mit <c>edit_status</c> 3 reicht zur externen ACE durch und fördert nie selbst.
    /// </summary>
    public static AceState ReadAce(JsonElement? boxes)
    {
        var types = new Dictionary<int, string>();
        if (boxes is not { ValueKind: JsonValueKind.Array } list) return new AceState(0, null, types);

        var all = list.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object).ToList();
        if (all.Count == 0) return new AceState(0, null, types);
        var ids = all.Select(b => (int)(b.GetDoubleOrNull("id") ?? 0)).ToList();
        var offset = -Math.Min(0, ids.Min());

        int count = 0;
        int? loaded = null;
        var loadedByStatus = new List<int>();
        var linked = new HashSet<int>();
        for (var b = 0; b < all.Count; b++)
        {
            var box = all[b];
            var first = (ids[b] + offset) * SlotsPerBox;
            count = Math.Max(count, first + SlotsPerBox);
            if (box.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in slots.EnumerateArray())
                {
                    var index = first + (int)(s.GetDoubleOrNull("index") ?? 0);
                    if (s.GetDoubleOrNull("edit_status") == LinkedSlot)
                    {
                        linked.Add(index);
                        continue;
                    }
                    if (s.GetStringOrNull("type") is { Length: > 0 } type) types[index] = type;
                    if (s.GetDoubleOrNull("status") == 5) loadedByStatus.Add(index);
                }
            }
            if (box.GetDoubleOrNull("loaded_slot") is double slot && slot >= 0 && !linked.Contains(first + (int)slot))
                loaded ??= first + (int)slot;
        }
        // Status 5 = eingezogen; nur eindeutig, wenn es genau ein Slot ist (der Kobra X hat mehrere zugleich im Kopf)
        if (loaded is null && loadedByStatus.Count == 1) loaded = loadedByStatus[0];
        return new AceState(count, loaded, types);
    }

    private static TrackedJob? ReadTracked(Printer printer)
    {
        if (string.IsNullOrEmpty(printer.AdapterStateJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<TrackedJob>(printer.AdapterStateJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
