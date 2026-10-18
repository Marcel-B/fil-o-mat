using System.Globalization;
using System.Net;
using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Prusa MK4, Core One, Mini, XL über PrusaLink (<c>/api/v1</c>). PrusaLink kennt keine Druckhistorie, daher merkt
/// sich der Adapter bei jedem Abgleich den laufenden Druck (<see cref="Printer.AdapterStateJson"/>) und meldet ihn,
/// sobald er beendet ist oder verschwindet. Gramm kommen aus den Slicer-Angaben der Datei, bei abgebrochenen Drucken
/// anteilig nach Fortschritt. Mit MMU3 gibt es Werte pro Slot.
/// </summary>
public class PrusaLinkAdapter(HttpClient http) : IPrinterAdapter
{
    /// <summary>Slots einer MMU3.</summary>
    public const int MmuSlots = 5;

    public PrinterType Type => PrinterType.PrusaLink;

    /// <summary>Laufender Druck, wie beim letzten Abgleich gesehen.</summary>
    public record TrackedJob(
        string JobId,
        string Name,
        double Progress,
        double? SlicedGrams,
        double? SlicedLengthMm,
        string? FilamentType,
        IReadOnlyList<double>? GramsPerTool,
        IReadOnlyList<double>? LengthPerTool);

    public async Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(Printer printer, DateTime since, CancellationToken cancellationToken)
    {
        if (printer.ToolCount is null)
        {
            using var info = await GetJsonAsync(printer, "api/v1/info", cancellationToken);
            printer.ToolCount = info?.RootElement.TryGetProperty("mmu", out var mmu) == true && mmu.ValueKind == JsonValueKind.True
                ? MmuSlots
                : 1;
        }

        var tracked = ReadTracked(printer);
        var finished = new List<PrintJob>();
        using var job = await GetJsonAsync(printer, "api/v1/job", cancellationToken);

        if (job is null)
        {
            // Kein Druck mehr: der gemerkte ist inzwischen beendet
            if (tracked is not null) finished.Add(Finish(tracked, tracked.Progress >= 99.5 ? "completed" : "cancelled", tracked.Progress));
            printer.AdapterStateJson = null;
            return finished;
        }

        var root = job.RootElement;
        var current = await ReadJobAsync(printer, root, tracked, cancellationToken);
        if (tracked is not null && tracked.JobId != current.JobId)
            finished.Add(Finish(tracked, tracked.Progress >= 99.5 ? "completed" : "cancelled", tracked.Progress));

        var state = root.GetStringOrNull("state");
        if (state is "FINISHED" or "STOPPED" or "ERROR")
        {
            finished.Add(state == "FINISHED"
                ? Finish(current, "completed", 100)
                : Finish(current, state == "STOPPED" ? "cancelled" : "error", current.Progress));
            printer.AdapterStateJson = null;
        }
        else
        {
            printer.AdapterStateJson = JsonSerializer.Serialize(current);
        }
        return finished;
    }

    private async Task<TrackedJob> ReadJobAsync(Printer printer, JsonElement job, TrackedJob? tracked, CancellationToken cancellationToken)
    {
        job.TryGetProperty("file", out var file);
        var name = file.GetStringOrNull("display_name") ?? file.GetStringOrNull("name") ?? "";
        // Job-IDs zählen nach einem Neustart wieder von vorn, daher mit dem Zeitstempel der Datei
        var jobId = $"{job.GetStringOrNull("id")}-{file.GetStringOrNull("m_timestamp")}";
        var progress = job.GetDoubleOrNull("progress") ?? 0;

        if (tracked is not null && tracked.JobId == jobId)
            return tracked with { Progress = Math.Max(tracked.Progress, progress) };

        // Slicer-Angaben stehen je nach Firmware direkt im Job oder nur bei der Datei
        file.TryGetProperty("meta", out var meta);
        if (meta.ValueKind != JsonValueKind.Object && file.GetStringOrNull("path") is { } path && file.GetStringOrNull("name") is { } fileName)
        {
            using var details = await GetJsonAsync(printer, $"api/v1/files/{path.Trim('/')}/{Uri.EscapeDataString(fileName)}", cancellationToken);
            if (details is not null && details.RootElement.TryGetProperty("meta", out var fileMeta)) meta = fileMeta.Clone();
        }

        return new TrackedJob(
            jobId,
            name,
            progress,
            meta.GetDoubleOrNull("filament used [g]"),
            meta.GetDoubleOrNull("filament used [mm]"),
            meta.GetStringOrNull("filament_type"),
            PerTool(meta, "filament used [g] per tool"),
            PerTool(meta, "filament used [mm] per tool"));
    }

    private static PrintJob Finish(TrackedJob job, string status, double progress)
    {
        var ratio = Math.Clamp(progress / 100, 0, 1);
        var sliced = job.SlicedLengthMm ?? job.LengthPerTool?.Sum();
        return new PrintJob(
            JobId: job.JobId,
            Name: job.Name,
            Status: status,
            EndedAt: DateTime.UtcNow,
            LengthMm: (sliced ?? 0) * ratio,
            SlicedLengthMm: sliced,
            SlicedGrams: job.SlicedGrams ?? job.GramsPerTool?.Sum(),
            FilamentType: job.FilamentType,
            // Nur bei mehreren Slots pro Slot buchen
            SlicedGramsPerFilament: job.GramsPerTool is { Count: > 1 } ? job.GramsPerTool : null,
            SlicedLengthPerFilament: job.GramsPerTool is { Count: > 1 } ? job.LengthPerTool : null);
    }

    /// <summary>Werte pro Slot als Liste oder als Text "1.2, 3.4".</summary>
    private static IReadOnlyList<double>? PerTool(JsonElement meta, string name)
    {
        if (meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Array) return meta.GetDoubleListOrNull(name);
        if (value.ValueKind != JsonValueKind.String) return null;
        return value.GetString()!.Split(',', ';')
            .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0)
            .ToList();
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

    /// <summary>JSON-Antwort oder <c>null</c> bei 204 (kein Druck).</summary>
    private async Task<JsonDocument?> GetJsonAsync(Printer printer, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{printer.BaseUrl.TrimEnd('/')}/{path}");
        // Buddy-Firmware (MK4, Core One, Mini, XL): API-Schlüssel aus den PrusaLink-Einstellungen am Drucker
        if (!string.IsNullOrEmpty(printer.ApiKey)) request.Headers.Add("X-Api-Key", printer.ApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException("PrusaLink lehnt den API-Schlüssel ab (Einstellungen > Netzwerk > PrusaLink am Drucker).", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
