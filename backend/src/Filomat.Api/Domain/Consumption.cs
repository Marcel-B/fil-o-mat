namespace Filomat.Api.Domain;

/// <summary>Verbrauch eines abgeschlossenen (oder abgebrochenen) Drucks.</summary>
public class Consumption
{
    public required string Id { get; set; }
    public required string PrinterId { get; set; }

    /// <summary>ID des Drucks beim Drucker; zusammen mit <see cref="PrinterId"/> eindeutig.</summary>
    public required string JobId { get; set; }

    public string JobName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime EndedAt { get; set; }

    /// <summary>Verbrauchte Länge in mm, wie vom Drucker gemeldet.</summary>
    public double LengthMm { get; set; }

    /// <summary>Verbrauch in Gramm (vom Drucker oder aus Länge, Durchmesser und Dichte berechnet).</summary>
    public double Grams { get; set; }

    /// <summary>Spule, von der abgebucht wurde; <c>null</c>, wenn sie nicht eindeutig war.</summary>
    public string? ItemId { get; set; }

    public DateTime RecordedAt { get; set; }
}
