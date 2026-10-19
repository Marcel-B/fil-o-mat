using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>Ein beendeter Druck, wie ihn ein Drucker meldet.</summary>
/// <param name="LengthMm">Tatsächlich verbrauchte Filamentlänge.</param>
/// <param name="SlicedLengthMm">Vom Slicer geplante Gesamtlänge, falls bekannt.</param>
/// <param name="SlicedGrams">Vom Slicer geplantes Gesamtgewicht, falls bekannt.</param>
public record PrintJob(
    string JobId,
    string Name,
    string Status,
    DateTime EndedAt,
    double LengthMm,
    double? SlicedLengthMm = null,
    double? SlicedGrams = null,
    string? FilamentType = null,
    IReadOnlyList<double>? SlicedGramsPerFilament = null,
    IReadOnlyList<double>? SlicedLengthPerFilament = null);

/// <summary>Welche Spule (Tag-UID) in welchem Werkzeugkopf steckt und welcher Slicer-Filament-Slot auf welchen Kopf geht.</summary>
/// <param name="TagUids">Pro Werkzeugkopf die UID des erkannten Tags (Hex, Großbuchstaben) oder <c>null</c>.</param>
/// <param name="ExtruderMap">Slicer-Filament-Index → Werkzeugkopf.</param>
/// <param name="Filaments">Pro Werkzeugkopf die Daten, die der Drucker vom Tag gelesen hat.</param>
public record ToolState(
    IReadOnlyList<string?> TagUids,
    IReadOnlyList<int> ExtruderMap,
    IReadOnlyList<DetectedFilament?>? Filaments = null)
{
    public int ExtruderFor(int filamentIndex) =>
        filamentIndex < ExtruderMap.Count ? ExtruderMap[filamentIndex] : filamentIndex;

    public string? TagFor(int extruder) => extruder >= 0 && extruder < TagUids.Count ? TagUids[extruder] : null;
}

/// <summary>Filamentdaten, die ein Drucker selbst von einem Tag gelesen hat.</summary>
public record DetectedFilament(string? Vendor, string? Type, string? SubType, string? ColorHex, int? MinTemp, int? MaxTemp, int? BedTemp);

/// <summary>Anbindung an einen Druckertyp.</summary>
public interface IPrinterAdapter
{
    PrinterType Type { get; }

    /// <summary>Beendete Drucke, die nach <paramref name="since"/> gestartet wurden, ältester zuerst.</summary>
    Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(Printer printer, DateTime since, CancellationToken cancellationToken);

    /// <summary>Spulen pro Werkzeugkopf, falls der Drucker sie selbst erkennt; sonst <c>null</c>.</summary>
    Task<ToolState?> GetToolStateAsync(Printer printer, CancellationToken cancellationToken) =>
        Task.FromResult<ToolState?>(null);
}
