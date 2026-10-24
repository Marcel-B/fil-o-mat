using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Bambu Lab P1S (auch P1P, X1, A1) im LAN. Der Drucker kennt weder eine Historie noch die geförderte Länge. Der
/// Adapter merkt sich deshalb den laufenden Druck (<see cref="Printer.AdapterStateJson"/>), holt bei dessen Beginn
/// die 3MF-Datei von der SD-Karte (Slicer-Gewicht pro Filament) und notiert bei jedem Abgleich, welcher AMS-Slot
/// gerade fördert. Am Ende wird jedes Filament dem Slot mit derselben Farbe zugeordnet, abgebrochene Drucke anteilig
/// nach Fortschritt. Ohne Datei bleibt die Restmenge, die das AMS für Bambu-Spulen meldet.
/// Slots: AMS-Einheit · 4 + Fach, danach die externe Spule; ohne AMS ein Slot.
/// </summary>
public class BambuAdapter(IBambuLan lan, ILogger<BambuAdapter> logger) : IPrinterAdapter
{
    public const int SlotsPerAms = 4;

    /// <summary><c>tray_now</c> der externen Spule.</summary>
    public const int ExternalTray = 254;

    /// <summary>So oft wird die Druckdatei gesucht, bevor nur noch die AMS-Restmenge zählt.</summary>
    private const int FileAttempts = 3;

    public PrinterType Type => PrinterType.Bambu;

    /// <summary>Fach im AMS bzw. externe Spule, wie zuletzt gemeldet.</summary>
    /// <param name="Remain">Restmenge in Prozent (nur Bambu-Spulen mit Chip), sonst -1.</param>
    /// <param name="Weight">Nenngewicht der Spule in Gramm.</param>
    public record Tray(string? Type, string? Color, double Remain, double Weight);

    /// <summary>Laufender Druck, wie beim letzten Abgleich gesehen.</summary>
    /// <param name="Key">Datei und Name, um einen anderen Druck zu erkennen.</param>
    /// <param name="Tray"><c>tray_now</c> beim letzten Abgleich.</param>
    /// <param name="ProgressPerTray">Fortschritt in Prozentpunkten pro <c>tray_now</c>.</param>
    /// <param name="RemainAtStart">AMS-Restmenge pro Fach beim ersten Blick auf den Druck.</param>
    /// <param name="Filaments">Slicer-Angaben aus der Druckdatei, falls gefunden.</param>
    public record TrackedJob(
        string JobId,
        string Key,
        string? TaskId,
        string Name,
        double Progress,
        int? Tray,
        Dictionary<int, double> ProgressPerTray,
        Dictionary<int, Tray> Trays,
        Dictionary<int, double> RemainAtStart,
        IReadOnlyList<SlicedFilament>? Filaments,
        int FileAttempts,
        DateTime? StartedAt = null);

    /// <summary>AMS-Einheiten, Fächer (Schlüssel wie <c>tray_now</c>) und das fördernde Fach.</summary>
    public record AmsState(int Units, Dictionary<int, Tray> Trays, int? Now);

    /// <summary>Meldung aus dem letzten Abgleich pro Drucker, damit der Zustand danach keine zweite MQTT-Verbindung braucht.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BambuReport> _reports = new();

    public async Task<PrinterStatus?> GetStatusAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (!_reports.TryRemove(printer.Id, out var report))
        {
            var (serial, accessCode) = Credentials(printer.ApiKey);
            report = await lan.QueryAsync(printer.BaseUrl, accessCode, serial, cancellationToken);
        }
        var print = report.Print;
        var ams = ReadAms(print);
        var slotCount = SlotCount(ams.Units);

        var heaters = new List<Heater>();
        if (print.GetDoubleOrNull("nozzle_temper") is { } nozzle)
            heaters.Add(new Heater("Düse", 0, nozzle, print.GetDoubleOrNull("nozzle_target_temper")));
        if (print.GetDoubleOrNull("bed_temper") is { } bed)
            heaters.Add(new Heater("Bett", null, bed, print.GetDoubleOrNull("bed_target_temper")));

        var slots = new List<SlotReport>();
        for (var slot = 0; slot < slotCount; slot++)
        {
            var tray = slotCount <= 1 ? ExternalTray : slot == slotCount - 1 ? ExternalTray : slot;
            var info = ams.Trays.GetValueOrDefault(tray);
            slots.Add(new SlotReport(slot, info is not null, info?.Type, info?.Color, info is { Remain: >= 0 } ? info.Remain : null));
        }

        var dryers = new List<DryerReport>();
        if (print.TryGetProperty("ams", out var amsRoot) && amsRoot.TryGetProperty("ams", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.GetDoubleOrNull("id") is not { } id || id is < 0 or >= 4) continue;
                var raw = unit.GetDoubleOrNull("humidity_raw");
                var level = unit.GetDoubleOrNull("humidity");
                var temperature = unit.GetDoubleOrNull("temp");
                if (raw is null && level is not (>= 1 and <= 5)) continue;
                dryers.Add(new DryerReport($"AMS {(int)id + 1}", raw, level is >= 1 and <= 5 ? (int)level : null,
                    temperature is > 0 ? temperature : null, Enumerable.Range((int)id * SlotsPerAms, SlotsPerAms).ToList()));
            }
        }

        var gcodeState = print.GetStringOrNull("gcode_state");
        var state = gcodeState switch
        {
            "PREPARE" or "SLICING" or "RUNNING" => PrintState.Printing,
            "PAUSE" => PrintState.Paused,
            "FINISH" => PrintState.Finished,
            "FAILED" => PrintState.Cancelled,
            _ => PrintState.Idle,
        };
        var error = print.GetDoubleOrNull("print_error") is { } code && code != 0 ? $"Fehlercode {code:X8}" : null;
        if (error is not null && state is PrintState.Paused or PrintState.Cancelled) state = PrintState.Error;
        int? active = ams.Now is { } now ? SlotOf(now, slotCount) : null;
        if (state == PrintState.Idle)
            return new PrinterStatus(state, Heaters: heaters, Slots: slots, Dryers: dryers);

        var tracked = ReadTracked(printer);
        var name = print.GetStringOrNull("subtask_name") ?? Path.GetFileNameWithoutExtension(print.GetStringOrNull("gcode_file") ?? "");
        IReadOnlyList<double>? grams = null;
        if (tracked?.Filaments is { Count: > 0 })
        {
            var planned = Finish(printer, tracked, "completed");
            grams = planned.SlicedGramsPerFilament ?? (planned.SlicedGrams is { } total ? [total] : null);
        }

        return new PrinterStatus(
            state,
            JobName: name,
            JobKey: $"{print.GetStringOrNull("gcode_file")}|{name}",
            Progress: state == PrintState.Finished ? 100 : print.GetDoubleOrNull("mc_percent"),
            // Minuten
            RemainingSeconds: print.GetDoubleOrNull("mc_remaining_time") is { } remain ? (int)(remain * 60) : null,
            Layer: (int?)print.GetDoubleOrNull("layer_num"),
            TotalLayers: (int?)print.GetDoubleOrNull("total_layer_num"),
            ActiveTool: active,
            Heaters: heaters,
            Slots: slots,
            Dryers: dryers,
            JobGramsPerTool: grams,
            Message: error,
            HasThumbnail: tracked?.Filaments is not null);
    }

    public async Task<Thumbnail?> GetThumbnailAsync(Printer printer, CancellationToken cancellationToken)
    {
        var (serial, accessCode) = Credentials(printer.ApiKey);
        var print = (await lan.QueryAsync(printer.BaseUrl, accessCode, serial, cancellationToken)).Print;
        var gcodeFile = print.GetStringOrNull("gcode_file");
        // Die 3MF ist mehrere MB groß; PrinterLive merkt sich das Bild pro Druck
        var file = await lan.DownloadAsync(printer.BaseUrl, accessCode, BambuLan.FileCandidates(gcodeFile, print.GetStringOrNull("subtask_name")), cancellationToken);
        return file is not null && BambuLan.ReadPlateImage(file, BambuLan.PlateOf(gcodeFile)) is { } png ? new Thumbnail(png, "image/png") : null;
    }

    public async Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(Printer printer, DateTime since, CancellationToken cancellationToken)
    {
        var (serial, accessCode) = Credentials(printer.ApiKey);
        var report = await lan.QueryAsync(printer.BaseUrl, accessCode, serial, cancellationToken);
        _reports[printer.Id] = report;
        var print = report.Print;
        var ams = ReadAms(print);
        printer.ToolCount = SlotCount(ams.Units);

        var tracked = ReadTracked(printer);
        var finished = new List<PrintJob>();
        var state = print.GetStringOrNull("gcode_state");
        var name = print.GetStringOrNull("subtask_name") ?? Path.GetFileNameWithoutExtension(print.GetStringOrNull("gcode_file") ?? "");
        var key = $"{print.GetStringOrNull("gcode_file")}|{name}";
        var taskId = print.GetStringOrNull("task_id") is { Length: > 0 } id && id != "0" ? id : null;
        var progress = print.GetDoubleOrNull("mc_percent") ?? 0;

        // Ein anderer Druck: der gemerkte ist vorbei, ohne dass ein Abgleich sein Ende sah
        if (tracked is not null && (tracked.Key != key
                                    || taskId is not null && tracked.TaskId is not null && taskId != tracked.TaskId
                                    || progress + 10 < tracked.Progress))
        {
            finished.Add(Finish(printer, tracked, Ended(tracked)));
            tracked = null;
        }

        switch (state)
        {
            case "PREPARE" or "SLICING" or "RUNNING" or "PAUSE":
                tracked ??= new TrackedJob(taskId ?? $"local-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29], key, taskId, name, 0, null, [], [], [], null, 0, DateTime.UtcNow);
                tracked = Advance(tracked, progress, taskId, ams);
                if (tracked.Filaments is null && tracked.FileAttempts < FileAttempts)
                    tracked = await LoadFileAsync(printer, accessCode, print, tracked, cancellationToken);
                printer.AdapterStateJson = JsonSerializer.Serialize(tracked);
                return finished;

            case "FINISH" or "FAILED" when tracked is not null:
                tracked = Advance(tracked, progress, taskId, ams);
                finished.Add(Finish(printer, tracked, state == "FINISH" ? "completed" : "cancelled"));
                break;

            // FINISH und FAILED bleiben stehen, bis der nächste Druck beginnt; ohne gemerkten Druck ist er schon gebucht
            default:
                if (tracked is not null) finished.Add(Finish(printer, tracked, Ended(tracked)));
                break;
        }
        printer.AdapterStateJson = null;
        return finished;
    }

    /// <summary>Zugangscode, optional mit Seriennummer davor: <c>01P00A123456789:12345678</c>.</summary>
    public static (string? Serial, string AccessCode) Credentials(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new PrinterUnavailableException("Zugangscode fehlt (am Drucker unter Einstellungen > WLAN).");
        var parts = apiKey.Trim().Split(':', 2);
        return parts.Length == 2 ? (parts[0].Trim(), parts[1].Trim()) : (null, parts[0]);
    }

    /// <summary>Vier Slots pro AMS und einer für die externe Spule; ohne AMS einer.</summary>
    public static int SlotCount(int amsUnits) => amsUnits > 0 ? amsUnits * SlotsPerAms + 1 : 1;

    /// <summary>Slot für <c>tray_now</c>: Fächer der Reihe nach, die externe Spule als letzter Slot.</summary>
    public static int SlotOf(int tray, int slots) => slots <= 1 ? 0 : tray == ExternalTray ? slots - 1 : tray;

    /// <summary>Rechnet den Fortschritt seit dem letzten Abgleich dem Fach zu, das beim letzten Abgleich förderte.</summary>
    private static TrackedJob Advance(TrackedJob job, double progress, string? taskId, AmsState ams)
    {
        var perTray = new Dictionary<int, double>(job.ProgressPerTray);
        var delta = progress - job.Progress;
        if (delta > 0 && (job.Tray ?? ams.Now) is { } tray)
            perTray[tray] = perTray.GetValueOrDefault(tray) + delta;

        var trays = new Dictionary<int, Tray>(job.Trays);
        var remainAtStart = new Dictionary<int, double>(job.RemainAtStart);
        foreach (var (index, info) in ams.Trays)
        {
            trays[index] = info;
            if (info.Remain >= 0) remainAtStart.TryAdd(index, info.Remain);
        }

        return job with
        {
            TaskId = job.TaskId ?? taskId,
            Progress = Math.Max(job.Progress, progress),
            Tray = ams.Now ?? job.Tray,
            ProgressPerTray = perTray,
            Trays = trays,
            RemainAtStart = remainAtStart,
        };
    }

    private async Task<TrackedJob> LoadFileAsync(Printer printer, string accessCode, JsonElement print, TrackedJob job, CancellationToken cancellationToken)
    {
        var gcodeFile = print.GetStringOrNull("gcode_file");
        try
        {
            var file = await lan.DownloadAsync(printer.BaseUrl, accessCode, BambuLan.FileCandidates(gcodeFile, print.GetStringOrNull("subtask_name")), cancellationToken);
            if (file is not null && BambuLan.ReadSliceInfo(file, BambuLan.PlateOf(gcodeFile)) is { } filaments)
                return job with { Filaments = filaments };
            logger.LogWarning("Druckdatei {File} von {Printer} nicht gefunden", gcodeFile ?? job.Name, printer.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Druckdatei von {Printer} nicht lesbar", printer.Name);
        }
        return job with { FileAttempts = job.FileAttempts + 1 };
    }

    internal static PrintJob Finish(Printer printer, TrackedJob job, string status)
    {
        var slots = printer.ToolCount ?? 1;
        int? duration = job.StartedAt is { } started ? (int)(DateTime.UtcNow - started).TotalSeconds : null;
        var ratio = status == "completed" ? 1 : Math.Clamp(job.Progress / 100, 0, 1);
        var grams = new double[slots];
        var lengths = new double[slots];

        if (job.Filaments?.Where(f => f.UsedGrams > 0).ToList() is { Count: > 0 } used)
        {
            var unassigned = new List<SlicedFilament>();
            var share = job.ProgressPerTray.Where(p => p.Value > 0).ToDictionary();
            foreach (var filament in used)
            {
                var trays = TraysFor(filament, used.Count, share, job.Trays);
                if (trays.Count == 0)
                {
                    unassigned.Add(filament);
                    continue;
                }
                var total = trays.Sum(t => t.Weight);
                foreach (var (tray, weight) in trays)
                {
                    var slot = SlotOf(tray, slots);
                    grams[slot] += filament.UsedGrams * weight / total;
                    lengths[slot] += filament.UsedMeters * 1000 * weight / total;
                }
            }

            var slicedGrams = used.Sum(f => f.UsedGrams);
            var slicedLength = used.Sum(f => f.UsedMeters) * 1000;
            string? type = used.Select(f => f.Type).Distinct().Count() == 1 ? used[0].Type : null;
            // Ohne AMS oder ohne jede Zuordnung: alles auf die Spule im Drucker
            if (slots <= 1 || unassigned.Count == used.Count)
                return new PrintJob(job.JobId, job.Name, status, DateTime.UtcNow, slicedLength * ratio, slicedLength, slicedGrams, type, DurationSeconds: duration);

            // Nicht zugeordnete Filamente anteilig auf die übrigen Slots
            var assigned = grams.Sum();
            var restGrams = unassigned.Sum(f => f.UsedGrams);
            var restLength = unassigned.Sum(f => f.UsedMeters) * 1000;
            var assignedLength = lengths.Sum();
            for (var slot = 0; slot < slots; slot++)
            {
                var part = grams[slot] / assigned;
                grams[slot] += restGrams * part;
                lengths[slot] += assignedLength > 0 ? restLength * lengths[slot] / assignedLength : 0;
            }
            return new PrintJob(job.JobId, job.Name, status, DateTime.UtcNow, slicedLength * ratio, slicedLength, slicedGrams, type, grams, lengths, DurationSeconds: duration);
        }

        // Ohne Druckdatei: was das AMS bei Bambu-Spulen weniger meldet
        foreach (var (tray, start) in job.RemainAtStart)
        {
            if (!job.Trays.TryGetValue(tray, out var info) || info.Remain < 0 || info.Weight <= 0 || start <= info.Remain) continue;
            var slot = SlotOf(tray, slots);
            grams[slot] += (start - info.Remain) / 100 * info.Weight;
            lengths[slot] += (start - info.Remain) / 100 * info.Weight / FilamentMath.GramsFromLength(1, info.Type);
        }
        var length = lengths.Sum();
        if (slots <= 1 || length <= 0)
            return new PrintJob(job.JobId, job.Name, status, DateTime.UtcNow, length, length > 0 ? length : null, length > 0 ? grams.Sum() : null, DurationSeconds: duration);
        return new PrintJob(job.JobId, job.Name, status, DateTime.UtcNow, length, length, grams.Sum(), null, grams, lengths, DurationSeconds: duration);
    }

    /// <summary>
    /// Fächer eines Filaments mit ihrem Anteil. Ein einziges Filament gehört zu allen Fächern, die gefördert haben
    /// (Nachfüllen aus einem zweiten Fach); sonst zählt die Farbe, bevorzugt unter den Fächern, die gefördert haben.
    /// </summary>
    private static List<(int Tray, double Weight)> TraysFor(SlicedFilament filament, int filamentCount, Dictionary<int, double> share, Dictionary<int, Tray> trays)
    {
        if (filamentCount == 1) return share.Select(p => (p.Key, p.Value)).ToList();
        if (filament.Color is null) return [];

        var sameColor = trays.Where(t => t.Value.Color == filament.Color).ToList();
        var candidates = sameColor.Where(t => share.ContainsKey(t.Key)).ToList();
        if (candidates.Count == 0) candidates = sameColor;
        if (candidates.Count > 1 && candidates.Where(t => SameType(t.Value.Type, filament.Type)).ToList() is { Count: > 0 } sameType)
            candidates = sameType;
        return candidates.Count == 1 ? [(candidates[0].Key, 1)] : candidates.Select(t => (t.Key, share.GetValueOrDefault(t.Key, 1))).ToList();
    }

    private static bool SameType(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Ein Druck, der ohne Abschlussmeldung verschwindet: fertig, wenn er fast durch war.</summary>
    private static string Ended(TrackedJob job) => job.Progress >= 99 ? "completed" : "cancelled";

    /// <summary>Fächer aus <c>ams.ams[].tray[]</c> und <c>vt_tray</c>; Schlüssel wie <c>tray_now</c>.</summary>
    public static AmsState ReadAms(JsonElement print)
    {
        var trays = new Dictionary<int, Tray>();
        var units = 0;
        int? now = null;
        if (print.TryGetProperty("ams", out var ams) && ams.ValueKind == JsonValueKind.Object)
        {
            if (ams.TryGetProperty("ams", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var unit in list.EnumerateArray())
                {
                    // AMS HT u. a. zählen ab 128 und bleiben außen vor
                    if (unit.GetDoubleOrNull("id") is not { } id || id is < 0 or >= 4) continue;
                    units = Math.Max(units, (int)id + 1);
                    if (!unit.TryGetProperty("tray", out var slots) || slots.ValueKind != JsonValueKind.Array) continue;
                    foreach (var slot in slots.EnumerateArray())
                    {
                        if (slot.GetDoubleOrNull("id") is not { } index || ReadTray(slot) is not { } tray) continue;
                        trays[(int)id * SlotsPerAms + (int)index] = tray;
                    }
                }
            }
            if (ams.GetDoubleOrNull("tray_now") is { } current && (current < units * SlotsPerAms || current == ExternalTray))
                now = (int)current;
        }
        if (print.TryGetProperty("vt_tray", out var external) && ReadTray(external) is { } spool)
            trays[ExternalTray] = spool;
        // Ohne AMS fördert immer die externe Spule
        if (units == 0) now = ExternalTray;
        return new AmsState(units, trays, now);
    }

    /// <summary>Ein leeres Fach meldet nur seine <c>id</c>.</summary>
    private static Tray? ReadTray(JsonElement tray)
    {
        if (tray.ValueKind != JsonValueKind.Object || tray.GetStringOrNull("tray_type") is not { Length: > 0 } type) return null;
        var color = tray.GetStringOrNull("tray_color") is { Length: >= 6 } rgba ? rgba[..6].ToUpperInvariant() : null;
        return new Tray(type, color, tray.GetDoubleOrNull("remain") ?? -1, tray.GetDoubleOrNull("tray_weight") ?? 0);
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
