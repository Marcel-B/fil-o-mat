using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>Liest die Druckhistorie von Moonraker (<c>/server/history/list</c>).</summary>
public class MoonrakerAdapter(HttpClient http) : IPrinterAdapter
{
    public virtual PrinterType Type => PrinterType.Moonraker;

    public virtual Task<ToolState?> GetToolStateAsync(Printer printer, CancellationToken cancellationToken) =>
        Task.FromResult<ToolState?>(null);

    /// <summary>Klipper-Objekte für den Zustand; Köpfe, die es nicht gibt, lässt Moonraker einfach weg.</summary>
    private const string StatusObjects = "print_stats&display_status&virtual_sdcard&toolhead&heater_bed&extruder&extruder1&extruder2&extruder3";

    /// <summary>Weitere Objekte, die ein Druckertyp für <see cref="ReadToolState"/> braucht.</summary>
    protected virtual string ExtraStatusObjects => "";

    /// <summary>Spulen pro Kopf aus der Objektabfrage, falls der Drucker sie erkennt.</summary>
    protected virtual ToolState? ReadToolState(JsonElement status) => null;

    /// <summary>Datei-Metadaten (Slicer-Gewichte, Vorschaubilder) pro Drucker und Datei; ändern sich während des Drucks nicht.</summary>
    private static readonly ConcurrentDictionary<string, JsonElement> MetadataCache = new();

    public async Task<PrinterStatus?> GetStatusAsync(Printer printer, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync(printer, $"printer/objects/query?{StatusObjects}{ExtraStatusObjects}", cancellationToken);
        if (!json.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
            return null;
        return await ReadStatusAsync(printer, status, cancellationToken);
    }

    private async Task<PrinterStatus> ReadStatusAsync(Printer printer, JsonElement status, CancellationToken cancellationToken)
    {
        status.TryGetProperty("print_stats", out var stats);
        var state = stats.GetStringOrNull("state") switch
        {
            "printing" => PrintState.Printing,
            "paused" => PrintState.Paused,
            "complete" => PrintState.Finished,
            "cancelled" => PrintState.Cancelled,
            "error" => PrintState.Error,
            _ => PrintState.Idle,
        };

        var heaters = new List<Heater>();
        for (var tool = 0; tool < 4; tool++)
        {
            if (status.TryGetProperty(tool == 0 ? "extruder" : $"extruder{tool}", out var extruder) && extruder.ValueKind == JsonValueKind.Object)
                heaters.Add(new Heater(tool == 0 && !status.TryGetProperty("extruder1", out _) ? "Düse" : $"Düse {tool + 1}", tool,
                    extruder.GetDoubleOrNull("temperature"), extruder.GetDoubleOrNull("target")));
        }
        if (status.TryGetProperty("heater_bed", out var bed) && bed.ValueKind == JsonValueKind.Object)
            heaters.Add(new Heater("Bett", null, bed.GetDoubleOrNull("temperature"), bed.GetDoubleOrNull("target")));

        status.TryGetProperty("toolhead", out var toolhead);
        int? activeTool = toolhead.GetStringOrNull("extruder") is { } name && name.StartsWith("extruder", StringComparison.Ordinal)
            ? int.TryParse(name["extruder".Length..], out var number) ? number : 0
            : null;

        var tools = ReadToolState(status);
        var slots = tools?.TagUids.Select((tag, tool) =>
        {
            var filament = tools.Filaments is { } list && tool < list.Count ? list[tool] : null;
            return new SlotReport(tool, tag is not null || filament?.Type is not null ? true : null, filament?.Type, filament?.ColorHex, TagUid: tag);
        }).ToList();

        var filename = stats.GetStringOrNull("filename") is { Length: > 0 } file ? file : null;
        if (filename is null || state == PrintState.Idle)
            return new PrinterStatus(state, Heaters: heaters, Slots: slots, ActiveTool: activeTool, Message: Message(stats));

        status.TryGetProperty("display_status", out var display);
        status.TryGetProperty("virtual_sdcard", out var sdcard);
        var progress = Math.Clamp((sdcard.GetDoubleOrNull("progress") ?? display.GetDoubleOrNull("progress") ?? 0) * 100, 0, 100);
        if (state == PrintState.Finished) progress = 100;
        var elapsed = stats.GetDoubleOrNull("print_duration");
        stats.TryGetProperty("info", out var info);

        var metadata = await MetadataAsync(printer, filename, cancellationToken);
        var estimated = metadata?.GetDoubleOrNull("estimated_time");
        double? remaining = progress is > 1 and < 100 && elapsed is > 0
            ? elapsed.Value / (progress / 100) - elapsed.Value
            : estimated is > 0 && state == PrintState.Printing ? estimated.Value * (1 - progress / 100) : null;

        return new PrinterStatus(
            state,
            JobName: Path.GetFileNameWithoutExtension(filename),
            JobKey: filename,
            Progress: Math.Round(progress, 1),
            RemainingSeconds: remaining is { } r ? (int)r : null,
            ElapsedSeconds: elapsed is { } e ? (int)e : null,
            Layer: (int?)info.GetDoubleOrNull("current_layer"),
            TotalLayers: (int?)(info.GetDoubleOrNull("total_layer") ?? metadata?.GetDoubleOrNull("layer_count")),
            ActiveTool: activeTool,
            Heaters: heaters,
            Slots: slots,
            JobGramsPerTool: metadata is { } meta ? GramsPerTool(meta, tools) : null,
            Message: Message(stats),
            HasThumbnail: metadata is { } m && LargestThumbnail(m) is not null);
    }

    private static string? Message(JsonElement stats) => stats.GetStringOrNull("message") is { Length: > 0 } message ? message : null;

    /// <summary>Slicer-Gramm pro Kopf: pro Filament-Slot über die Zuordnung des Druckers, sonst alles auf Kopf 0.</summary>
    internal static IReadOnlyList<double>? GramsPerTool(JsonElement metadata, ToolState? tools)
    {
        if (metadata.GetDoubleListOrNull("filament_weight") is { Count: > 0 } perFilament && perFilament.Any(g => g > 0))
        {
            var perTool = new double[Math.Max(tools?.TagUids.Count ?? 1, 1)];
            for (var i = 0; i < perFilament.Count; i++)
            {
                var tool = tools?.ExtruderFor(i) ?? 0;
                if (tool >= perTool.Length) Array.Resize(ref perTool, tool + 1);
                perTool[tool] += perFilament[i];
            }
            return perTool;
        }
        return metadata.GetDoubleOrNull("filament_weight_total") is double total and > 0 ? [total] : null;
    }

    private async Task<JsonElement?> MetadataAsync(Printer printer, string filename, CancellationToken cancellationToken)
    {
        var key = $"{printer.BaseUrl}|{filename}";
        if (MetadataCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            using var json = await GetJsonAsync(printer, $"server/files/metadata?filename={Uri.EscapeDataString(filename)}", cancellationToken);
            if (!json.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
            if (MetadataCache.Count > 100) MetadataCache.Clear();
            return MetadataCache[key] = result.Clone();
        }
        catch (HttpRequestException)
        {
            // Datei gelöscht oder noch nicht analysiert
            return null;
        }
    }

    /// <summary>Pfad des größten Vorschaubilds, relativ zum Ordner der Datei.</summary>
    private static string? LargestThumbnail(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array) return null;
        return thumbnails.EnumerateArray()
            .Where(t => t.GetStringOrNull("relative_path") is { Length: > 0 })
            .OrderByDescending(t => (t.GetDoubleOrNull("width") ?? 0) * (t.GetDoubleOrNull("height") ?? 0))
            .Select(t => t.GetStringOrNull("relative_path"))
            .FirstOrDefault();
    }

    public async Task<Thumbnail?> GetThumbnailAsync(Printer printer, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync(printer, "printer/objects/query?print_stats", cancellationToken);
        if (!json.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("status", out var status)
            || !status.TryGetProperty("print_stats", out var stats) || stats.GetStringOrNull("filename") is not { Length: > 0 } filename)
            return null;
        if (await MetadataAsync(printer, filename, cancellationToken) is not { } metadata || LargestThumbnail(metadata) is not { } relative)
            return null;

        var folder = Path.GetDirectoryName(filename)?.Replace('\\', '/') ?? "";
        var path = string.Join('/', (folder.Length > 0 ? $"{folder}/{relative}" : relative).Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{printer.BaseUrl.TrimEnd('/')}/server/files/gcodes/{path}");
        if (!string.IsNullOrEmpty(printer.ApiKey)) request.Headers.Add("X-Api-Key", printer.ApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return new Thumbnail(await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Content.Headers.ContentType?.MediaType ?? "image/png");
    }

    protected async Task<JsonDocument> GetJsonAsync(Printer printer, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{printer.BaseUrl.TrimEnd('/')}/{path}");
        if (!string.IsNullOrEmpty(printer.ApiKey)) request.Headers.Add("X-Api-Key", printer.ApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(
        Printer printer, DateTime since, CancellationToken cancellationToken)
    {
        var unixSince = new DateTimeOffset(since).ToUnixTimeSeconds();
        using var json = await GetJsonAsync(printer, $"server/history/list?order=asc&limit=100&since={unixSince}", cancellationToken);

        var jobs = new List<PrintJob>();
        if (!json.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("jobs", out var jobArray))
            return jobs;

        foreach (var job in jobArray.EnumerateArray())
        {
            var status = job.GetStringOrNull("status") ?? "";
            var endTime = job.GetDoubleOrNull("end_time");
            // Laufende Drucke haben noch kein Ende
            if (endTime is null || status == "in_progress") continue;

            job.TryGetProperty("metadata", out var metadata);
            jobs.Add(new PrintJob(
                JobId: job.GetStringOrNull("job_id") ?? "",
                Name: job.GetStringOrNull("filename") ?? "",
                Status: status,
                EndedAt: DateTimeOffset.FromUnixTimeMilliseconds((long)(endTime.Value * 1000)).UtcDateTime,
                LengthMm: job.GetDoubleOrNull("filament_used") ?? 0,
                SlicedLengthMm: metadata.GetDoubleOrNull("filament_total"),
                SlicedGrams: metadata.GetDoubleOrNull("filament_weight_total"),
                FilamentType: metadata.GetStringOrNull("filament_type"),
                // Orca/Prusa-Slicer: Werte pro Filament-Slot (Mehrfarbdruck, Werkzeugwechsler)
                SlicedGramsPerFilament: metadata.GetDoubleListOrNull("filament_weight"),
                SlicedLengthPerFilament: metadata.GetDoubleListOrNull("filament_used_mm"),
                DurationSeconds: job.GetDoubleOrNull("print_duration") is { } duration ? (int)duration : null));
        }
        return jobs;
    }
}

internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    public static IReadOnlyList<double>? GetDoubleListOrNull(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
            return null;
        return value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0).ToList();
    }

    public static double? GetDoubleOrNull(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
