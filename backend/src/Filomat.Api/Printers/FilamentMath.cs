namespace Filomat.Api.Printers;

public static class FilamentMath
{
    public const double DefaultDiameterMm = 1.75;

    /// <summary>Typische Dichten in g/cm³.</summary>
    private static readonly Dictionary<string, double> Densities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLA"] = 1.24,
        ["PETG"] = 1.27,
        ["ABS"] = 1.04,
        ["ASA"] = 1.07,
        ["TPU"] = 1.21,
        ["PA"] = 1.14,
        ["PC"] = 1.20,
    };

    public static double Density(string? type) =>
        type is not null && Densities.TryGetValue(type.Trim(), out var density) ? density : Densities["PLA"];

    /// <summary>Gramm aus einer Filamentlänge.</summary>
    public static double GramsFromLength(double lengthMm, string? type, double diameterMm = DefaultDiameterMm)
    {
        var radius = diameterMm / 2;
        var volumeCm3 = lengthMm * Math.PI * radius * radius / 1000;
        return volumeCm3 * Density(type);
    }

    /// <summary>
    /// Verbrauch eines Drucks in Gramm. Kennt der Slicer Länge und Gewicht, wird dessen Verhältnis genutzt
    /// (berücksichtigt Dichte und Durchmesser aus dem Slicer), sonst die Dichte des Filamenttyps.
    /// </summary>
    public static double GramsForJob(PrintJob job, string? itemType)
    {
        if (job.SlicedGrams is > 0 && job.SlicedLengthMm is > 0)
            return job.SlicedGrams.Value * job.LengthMm / job.SlicedLengthMm.Value;
        return GramsFromLength(job.LengthMm, itemType ?? job.FilamentType);
    }
}
