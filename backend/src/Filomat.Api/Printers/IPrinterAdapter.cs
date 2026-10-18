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
    string? FilamentType = null);

/// <summary>Anbindung an einen Druckertyp.</summary>
public interface IPrinterAdapter
{
    PrinterType Type { get; }

    /// <summary>Beendete Drucke, die nach <paramref name="since"/> gestartet wurden, ältester zuerst.</summary>
    Task<IReadOnlyList<PrintJob>> GetFinishedJobsAsync(Printer printer, DateTime since, CancellationToken cancellationToken);
}
