using System.Text.Json.Serialization;

namespace Filomat.Api.Printers;

[JsonConverter(typeof(JsonStringEnumConverter<PrintState>))]
public enum PrintState
{
    /// <summary>Bereit, kein Druck.</summary>
    Idle,

    /// <summary>Druckt (auch Aufheizen, Kalibrieren).</summary>
    Printing,

    Paused,

    /// <summary>Druck fertig, der Drucker zeigt ihn noch an.</summary>
    Finished,

    /// <summary>Druck abgebrochen, der Drucker zeigt ihn noch an.</summary>
    Cancelled,

    /// <summary>Fehler oder Eingriff nötig (z. B. Filament leer).</summary>
    Error,

    /// <summary>Nicht erreichbar.</summary>
    Offline,
}

/// <summary>Heizung mit Ist- und Solltemperatur.</summary>
/// <param name="Tool">Werkzeugkopf bei Düsen, <c>null</c> beim Bett oder der Kammer.</param>
public record Heater(string Name, int? Tool, double? Actual, double? Target);

/// <summary>Was der Drucker selbst über einen Kopf bzw. Slot weiß.</summary>
/// <param name="Loaded">Spule steckt laut Drucker; <c>null</c>, wenn er es nicht weiß.</param>
/// <param name="ColorHex"><c>RRGGBB</c>, Großbuchstaben.</param>
/// <param name="RemainPercent">Restmenge in Prozent (Bambu-Spulen mit Chip).</param>
public record SlotReport(int Tool, bool? Loaded, string? Type, string? ColorHex, double? RemainPercent = null, string? TagUid = null);

/// <summary>Trockenbox bzw. Spulenwechsler mit Feuchtesensor (ACE, AMS).</summary>
/// <param name="HumidityPercent">Relative Feuchte in Prozent, falls gemeldet.</param>
/// <param name="HumidityLevel">Bambu-Stufe 1 (feucht) bis 5 (trocken), falls nur sie gemeldet wird.</param>
/// <param name="Tools">Slots in dieser Box.</param>
public record DryerReport(string Name, double? HumidityPercent, int? HumidityLevel, double? Temperature, IReadOnlyList<int> Tools);

/// <summary>Zustand eines Druckers, wie er ihn gerade meldet.</summary>
/// <param name="JobKey">Erkennt einen anderen Druck, z. B. für das Vorschaubild.</param>
/// <param name="Progress">Fortschritt 0–100.</param>
/// <param name="JobGramsPerTool">Geplanter Verbrauch des ganzen Drucks pro Kopf (Index = Kopf).</param>
/// <param name="JobGramsEstimated">Der Verbrauch ist aus dem bisherigen Verbrauch hochgerechnet, nicht vom Slicer.</param>
/// <param name="Message">Fehlermeldung des Druckers oder warum er nicht erreichbar ist.</param>
public record PrinterStatus(
    PrintState State,
    string? JobName = null,
    string? JobKey = null,
    double? Progress = null,
    int? RemainingSeconds = null,
    int? ElapsedSeconds = null,
    int? Layer = null,
    int? TotalLayers = null,
    int? ActiveTool = null,
    IReadOnlyList<Heater>? Heaters = null,
    IReadOnlyList<SlotReport>? Slots = null,
    IReadOnlyList<DryerReport>? Dryers = null,
    IReadOnlyList<double>? JobGramsPerTool = null,
    bool JobGramsEstimated = false,
    string? Message = null,
    bool HasThumbnail = false)
{
    public bool IsActive => State is PrintState.Printing or PrintState.Paused;
}

/// <summary>Vorschaubild des laufenden Drucks.</summary>
public record Thumbnail(byte[] Data, string ContentType);
