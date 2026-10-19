using System.Text.Json.Serialization;

namespace Filomat.Api.Domain;

/// <summary>
/// Filament mit RFID-Tag; gehört über <see cref="ContainerId"/> zu höchstens einem Container.
/// Das Tag kann auf der Spule oder auf einem Musterplättchen kleben; im zweiten Fall steht es für eine
/// Filamentsorte mit angebrochenen Spulen (<see cref="Spool"/>) und Vorrat (<see cref="SpoolsInStock"/>).
/// </summary>
public class Item
{
    public required string Id { get; set; }
    public required string RfidTag { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? ContainerId { get; set; }
    public FilamentData? ItemData { get; set; }

    /// <summary>Filamentgewicht einer vollen Spule in Gramm (ohne Spule), z. B. 1000.</summary>
    public double? NetWeight { get; set; }

    /// <summary>Gewicht der leeren Spule in Gramm, um gewogene Spulen umzurechnen.</summary>
    public double? SpoolWeight { get; set; }

    /// <summary>Volle, noch nicht angebrochene Spulen dieser Sorte; <c>null</c>, wenn kein Vorrat geführt wird.</summary>
    public int? SpoolsInStock { get; set; }

    /// <summary>
    /// Lichtdurchlässigkeit als Transmission Distance (TD, HueForge) in mm, gemessen z. B. mit einem TD-1;
    /// je höher, desto mehr Licht lässt das Filament durch. <c>null</c>, wenn nicht gemessen.
    /// </summary>
    public double? TransmissionDistance { get; set; }

}

/// <summary>Filament-Daten im OpenSpool-Format, wie sie auf dem NFC-Tag stehen (daher snake_case).</summary>
public class FilamentData
{
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("color_hex")] public string? ColorHex { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("min_temp")] public string? MinTemp { get; set; }
    [JsonPropertyName("max_temp")] public string? MaxTemp { get; set; }
    [JsonPropertyName("bed_min_temp")] public string? BedMinTemp { get; set; }
    [JsonPropertyName("bed_max_temp")] public string? BedMaxTemp { get; set; }
    [JsonPropertyName("variant")] public string? Variant { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
}
