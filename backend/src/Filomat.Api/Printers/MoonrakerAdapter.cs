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
                SlicedLengthPerFilament: metadata.GetDoubleListOrNull("filament_used_mm")));
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
