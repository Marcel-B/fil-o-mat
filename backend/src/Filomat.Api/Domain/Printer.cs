namespace Filomat.Api.Domain;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PrinterType>))]
public enum PrinterType
{
    /// <summary>Klipper mit Moonraker (Voron, …).</summary>
    Moonraker,

    /// <summary>Snapmaker U1 (Moonraker); erkennt die Spulen pro Werkzeugkopf über deren NFC-Tags.</summary>
    SnapmakerU1,
}

/// <summary>Konfigurierter Drucker. Jeder Drucker hat einen eigenen Container für die eingelegten Spulen.</summary>
public class Printer
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public PrinterType Type { get; set; }

    /// <summary>Adresse, z. B. <c>http://voron.local</c> oder <c>http://192.168.1.50:7125</c>.</summary>
    public required string BaseUrl { get; set; }

    /// <summary>API-Schlüssel bzw. Zugangscode, falls der Drucker einen verlangt.</summary>
    public string? ApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Container, in den die Spulen gelegt werden, die gerade in diesem Drucker stecken.</summary>
    public required string ContainerId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Ende des zuletzt übernommenen Drucks; ältere Drucke werden nicht erneut abgefragt.</summary>
    public DateTime? LastJobEndedAt { get; set; }

    /// <summary>Zuletzt gemeldete Spulen pro Werkzeugkopf (JSON), damit ein Druck den Spulen während des Drucks zugeordnet wird.</summary>
    public string? ToolStateJson { get; set; }

    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }
}
