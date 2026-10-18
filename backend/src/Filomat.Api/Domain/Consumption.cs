namespace Filomat.Api.Domain;

/// <summary>Verbrauch eines abgeschlossenen (oder abgebrochenen) Drucks.</summary>
public class Consumption
{
    public required string Id { get; set; }
    public required string PrinterId { get; set; }

    /// <summary>ID des Drucks beim Drucker; zusammen mit <see cref="PrinterId"/> und <see cref="Tool"/> eindeutig.</summary>
    public required string JobId { get; set; }

    public string JobName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime EndedAt { get; set; }

    /// <summary>Verbrauchte Länge in mm, wie vom Drucker gemeldet.</summary>
    public double LengthMm { get; set; }

    /// <summary>Verbrauch in Gramm (vom Drucker oder aus Länge, Durchmesser und Dichte berechnet).</summary>
    public double Grams { get; set; }

    /// <summary>Werkzeugkopf bei Druckern mit mehreren Köpfen, sonst 0.</summary>
    public int Tool { get; set; }

    /// <summary>UID des Tags, das der Drucker in diesem Kopf erkannt hat, falls bekannt.</summary>
    public string? TagUid { get; set; }

    /// <summary>Angebrochene Spule, von der abgebucht wurde.</summary>
    public string? SpoolId { get; set; }

    /// <summary>Sorte, von der abgebucht wurde; <c>null</c>, wenn sie nicht eindeutig war.</summary>
    public string? ItemId { get; set; }

    public DateTime RecordedAt { get; set; }
}
