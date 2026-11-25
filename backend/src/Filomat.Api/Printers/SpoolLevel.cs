namespace Filomat.Api.Printers;

/// <summary>Wann eine Spule als fast leer gilt; dieselbe Schwelle für Meldungen und Messwerte.</summary>
public static class SpoolLevel
{
    /// <summary>Unter 10 % der vollen Spule oder unter 50 g.</summary>
    public static bool IsLow(double remaining, double? netWeight) => remaining < Math.Max(50, (netWeight ?? 0) * 0.1);
}
