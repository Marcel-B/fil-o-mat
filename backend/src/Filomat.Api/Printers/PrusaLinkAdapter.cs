using System.Globalization;
using System.Net;
using System.Text.Json;
using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Prusa MK4, Core One, Mini, XL über PrusaLink (<c>/api/v1</c>). PrusaLink kennt keine Druckhistorie, daher merkt
/// sich der Adapter bei jedem Abgleich den laufenden Druck (<see cref="Printer.AdapterStateJson"/>) und meldet ihn,
/// sobald er beendet ist oder verschwindet. Gramm kommen aus den Slicer-Angaben der Druckdatei, bei abgebrochenen Drucken
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
        IReadOnlyList<double>? LengthPerTool,
        double? TimePrinting = null,
        double? TimeRemaining = null,
        DateTime? SeenAt = null);

    /// <summary>
    /// Ob ein verschwundener Druck fertig geworden ist. Der Abgleich läuft nur alle paar Minuten, und nach dem Ende
    /// (Druckplatte entnommen, Drucker ausgeschaltet) meldet PrusaLink oft keinen Druck mehr: der letzte gesehene
    /// Fortschritt liegt dann knapp unter 100 %. Fertig ist der Druck, wenn die beim letzten Abgleich gemeldete
    /// Restzeit inzwischen abgelaufen war und nur noch wenig fehlte.
    /// </summary>
    public static bool LooksFinished(TrackedJob job, DateTime now)
    {
        if (job.Progress >= 99.5) return true;
        if (job.TimeRemaining is not { } remaining || remaining < 0 || job.SeenAt is not { } seen) return false;
        var nearlyDone = job.Progress >= 95 || remaining <= TimeSpan.FromMinutes(15).TotalSeconds;
        return nearlyDone && now >= seen.AddSeconds(remaining) - TimeSpan.FromMinutes(1);
    }

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
            // Kein Druck mehr: der gemerkte ist inzwischen beendet. Steht der Drucker noch auf „fertig“ oder
            // „abgebrochen“, gilt das, sonst entscheidet die Restzeit vom letzten Abgleich.
            if (tracked is not null)
            {
                var printerState = await GetPrinterStateAsync(printer, cancellationToken);
                finished.Add(printerState switch
                {
                    "FINISHED" => Finish(tracked, "completed", 100),
                    "STOPPED" => Finish(tracked, "cancelled", tracked.Progress),
                    _ when LooksFinished(tracked, DateTime.UtcNow) => Finish(tracked, "completed", 100),
                    _ => Finish(tracked, "cancelled", tracked.Progress),
                });
            }
            printer.AdapterStateJson = null;
            return finished;
        }

        var root = job.RootElement;
        var current = await ReadJobAsync(printer, root, tracked, cancellationToken);
        if (tracked is not null && tracked.JobId != current.JobId)
            finished.Add(LooksFinished(tracked, DateTime.UtcNow) ? Finish(tracked, "completed", 100) : Finish(tracked, "cancelled", tracked.Progress));

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

    public async Task<PrinterStatus?> GetStatusAsync(Printer printer, CancellationToken cancellationToken)
    {
        using var status = await GetJsonAsync(printer, "api/v1/status", cancellationToken);
        if (status is null || !status.RootElement.TryGetProperty("printer", out var state)) return null;

        var heaters = new List<Heater>
        {
            new("Düse", 0, state.GetDoubleOrNull("temp_nozzle"), state.GetDoubleOrNull("target_nozzle")),
            new("Bett", null, state.GetDoubleOrNull("temp_bed"), state.GetDoubleOrNull("target_bed")),
        };
        var printState = state.GetStringOrNull("state") switch
        {
            "PRINTING" or "BUSY" => PrintState.Printing,
            "PAUSED" => PrintState.Paused,
            "FINISHED" => PrintState.Finished,
            "STOPPED" => PrintState.Cancelled,
            "ERROR" or "ATTENTION" => PrintState.Error,
            _ => PrintState.Idle,
        };
        status.RootElement.TryGetProperty("job", out var progress);
        if (progress.ValueKind != JsonValueKind.Object)
            return new PrinterStatus(printState == PrintState.Printing ? PrintState.Idle : printState, Heaters: heaters);

        using var job = await GetJsonAsync(printer, "api/v1/job", cancellationToken);
        var root = job?.RootElement;
        JsonElement file = default;
        root?.TryGetProperty("file", out file);
        var tracked = ReadTracked(printer);
        var gramsPerTool = tracked?.GramsPerTool is { Count: > 0 } perTool && perTool.Any(g => g > 0)
            ? perTool
            : tracked?.SlicedGrams is > 0 ? [tracked.SlicedGrams.Value] : null;

        return new PrinterStatus(
            printState,
            JobName: file.GetStringOrNull("display_name") ?? file.GetStringOrNull("name") ?? tracked?.Name,
            JobKey: progress.GetStringOrNull("id") is { } id ? $"{id}-{file.GetStringOrNull("m_timestamp")}" : null,
            Progress: printState == PrintState.Finished ? 100 : progress.GetDoubleOrNull("progress"),
            RemainingSeconds: (int?)progress.GetDoubleOrNull("time_remaining"),
            ElapsedSeconds: (int?)progress.GetDoubleOrNull("time_printing"),
            Heaters: heaters,
            JobGramsPerTool: gramsPerTool,
            HasThumbnail: ThumbnailPath(file) is not null);
    }

    private static string? ThumbnailPath(JsonElement file) =>
        file.ValueKind == JsonValueKind.Object && file.TryGetProperty("refs", out var refs)
            ? refs.GetStringOrNull("thumbnail") ?? refs.GetStringOrNull("icon")
            : null;

    public async Task<Thumbnail?> GetThumbnailAsync(Printer printer, CancellationToken cancellationToken)
    {
        using var job = await GetJsonAsync(printer, "api/v1/job", cancellationToken);
        if (job is null || !job.RootElement.TryGetProperty("file", out var file) || ThumbnailPath(file) is not { } path) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{printer.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
        if (!string.IsNullOrEmpty(printer.ApiKey)) request.Headers.Add("X-Api-Key", printer.ApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return new Thumbnail(await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Content.Headers.ContentType?.MediaType ?? "image/png");
    }

    private async Task<TrackedJob> ReadJobAsync(Printer printer, JsonElement job, TrackedJob? tracked, CancellationToken cancellationToken)
    {
        job.TryGetProperty("file", out var file);
        var name = file.GetStringOrNull("display_name") ?? file.GetStringOrNull("name") ?? "";
        // Job-IDs zählen nach einem Neustart wieder von vorn, daher mit dem Zeitstempel der Datei
        var jobId = $"{job.GetStringOrNull("id")}-{file.GetStringOrNull("m_timestamp")}";
        var progress = job.GetDoubleOrNull("progress") ?? 0;

        var timePrinting = job.GetDoubleOrNull("time_printing");
        var timeRemaining = job.GetDoubleOrNull("time_remaining");
        var seenAt = DateTime.UtcNow;
        // Ohne Slicer-Angaben (z. B. Druck, der vor einem Update begonnen hat) noch einmal nachschlagen
        if (tracked is not null && tracked.JobId == jobId && (tracked.SlicedGrams is not null || tracked.SlicedLengthMm is not null))
            return tracked with
            {
                Progress = Math.Max(tracked.Progress, progress),
                TimePrinting = timePrinting ?? tracked.TimePrinting,
                TimeRemaining = timeRemaining,
                SeenAt = seenAt,
            };

        var usage = await ReadUsageAsync(printer, file, cancellationToken);
        return new TrackedJob(
            jobId,
            name,
            tracked is not null && tracked.JobId == jobId ? Math.Max(tracked.Progress, progress) : progress,
            usage?.Grams,
            usage?.LengthMm,
            usage?.FilamentType,
            usage?.GramsPerTool,
            usage?.LengthPerTool,
            timePrinting,
            timeRemaining,
            seenAt);
    }

    /// <summary>Filamentverbrauch laut Slicer, für den ganzen Druck und (bei MMU3) pro Slot.</summary>
    private record Usage(double? Grams, double? LengthMm, string? FilamentType, IReadOnlyList<double>? GramsPerTool, IReadOnlyList<double>? LengthPerTool);

    /// <summary>
    /// Slicer-Angaben zum Druck. PrusaLink auf Buddy-Firmware (MK4, Core One, Mini, XL) liefert weder beim Job noch bei der
    /// Datei Metadaten, daher werden sie zuletzt aus dem Kopf der Druckdatei selbst gelesen (nur <c>.bgcode</c>).
    /// </summary>
    private async Task<Usage?> ReadUsageAsync(Printer printer, JsonElement file, CancellationToken cancellationToken)
    {
        file.TryGetProperty("meta", out var meta);
        if (UsageFrom(Values(meta)) is { } fromJob) return fromJob;

        var path = file.GetStringOrNull("path");
        var fileName = file.GetStringOrNull("name");
        if (path is not null && fileName is not null)
        {
            try
            {
                using var details = await GetJsonAsync(printer, $"api/v1/files/{path.Trim('/')}/{Uri.EscapeDataString(fileName)}", cancellationToken);
                if (details is not null && details.RootElement.TryGetProperty("meta", out var fileMeta)
                    && UsageFrom(Values(fileMeta)) is { } fromFile)
                    return fromFile;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException && !cancellationToken.IsCancellationRequested)
            {
                // Dann eben aus der Datei
            }
        }

        file.TryGetProperty("refs", out var refs);
        var download = refs.GetStringOrNull("download") ?? (path is not null && fileName is not null ? $"{path.TrimEnd('/')}/{fileName}" : null);
        return download is null ? null : UsageFrom(await ReadFileHeaderAsync(printer, download, cancellationToken));
    }

    /// <summary>Metadaten aus dem Anfang der Druckdatei, ohne sie ganz herunterzuladen.</summary>
    private async Task<IReadOnlyDictionary<string, string>?> ReadFileHeaderAsync(Printer printer, string download, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{printer.BaseUrl.TrimEnd('/')}/{download.TrimStart('/')}");
            if (!string.IsNullOrEmpty(printer.ApiKey)) request.Headers.Add("X-Api-Key", printer.ApiKey);
            // Der Zeitrahmen des HttpClient gilt nur bis zu den Kopfzeilen, das Lesen bekommt einen eigenen
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await BgcodeMetadata.ReadAsync(stream, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>JSON-Metadaten als Text, Listen als "1.2, 3.4".</summary>
    private static Dictionary<string, string>? Values(JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in meta.EnumerateObject())
        {
            var text = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.Array => string.Join(", ", property.Value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "0")),
                _ => null,
            };
            if (text is not null) values[property.Name] = text;
        }
        return values;
    }

    /// <summary>
    /// Verbrauch aus den Slicer-Schlüsseln. Mit MMU3 stehen pro Slot Werte durch Kommas getrennt im selben Schlüssel
    /// (manche Firmware meldet sie zusätzlich unter „… per tool“).
    /// </summary>
    private static Usage? UsageFrom(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null) return null;
        var grams = Numbers(values, "filament used [g]");
        var length = Numbers(values, "filament used [mm]");
        if (grams is not { Count: > 0 } && length is not { Count: > 0 }) return null;
        var gramsPerTool = Numbers(values, "filament used [g] per tool") ?? (grams is { Count: > 1 } ? grams : null);
        var lengthPerTool = Numbers(values, "filament used [mm] per tool") ?? (length is { Count: > 1 } ? length : null);
        var type = values.GetValueOrDefault("filament_type")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return new Usage(grams?.Sum(), length?.Sum(), type, gramsPerTool, lengthPerTool);
    }

    /// <summary>Zahl oder Liste "1.2, 3.4" bzw. "1.2;3.4".</summary>
    private static IReadOnlyList<double>? Numbers(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var text) || text.Length == 0) return null;
        return text.Split(',', ';')
            .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0)
            .ToList();
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
            SlicedLengthPerFilament: job.GramsPerTool is { Count: > 1 } ? job.LengthPerTool : null,
            DurationSeconds: job.TimePrinting is { } seconds ? (int)seconds : null);
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

    /// <summary>Zustand aus <c>/api/v1/status</c> (z. B. FINISHED, STOPPED, IDLE) oder <c>null</c>.</summary>
    private async Task<string?> GetPrinterStateAsync(Printer printer, CancellationToken cancellationToken)
    {
        try
        {
            using var status = await GetJsonAsync(printer, "api/v1/status", cancellationToken);
            return status is not null && status.RootElement.TryGetProperty("printer", out var state) ? state.GetStringOrNull("state") : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException && !cancellationToken.IsCancellationRequested)
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
