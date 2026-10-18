namespace Filomat.Api.Domain;

/// <summary>
/// Eine angebrochene Spule einer Filamentsorte (<see cref="Item"/>). Mehrere Spulen derselben Sorte können
/// gleichzeitig angebrochen sein, z. B. in zwei Köpfen des U1 und in einem zweiten Drucker.
/// </summary>
public class Spool
{
    public required string Id { get; set; }
    public required string ItemId { get; set; }

    /// <summary>Laufende Nummer pro Sorte, z. B. zum Beschriften der Spule („Weiß #3“).</summary>
    public int Number { get; set; }

    /// <summary>Verbleibendes Filament in Gramm; <c>null</c>, wenn das Füllgewicht nicht bekannt ist.</summary>
    public double? RemainingWeight { get; set; }

    /// <summary>Drucker, in dem die Spule steckt; <c>null</c>, wenn sie im Lager liegt.</summary>
    public string? PrinterId { get; set; }

    /// <summary>Werkzeugkopf im Drucker (0 bei Druckern mit einem Kopf).</summary>
    public int? Tool { get; set; }

    public DateTime OpenedAt { get; set; }

    /// <summary>Zeitpunkt, an dem die Spule leer wurde; leere Spulen bleiben nur für den Verlauf erhalten.</summary>
    public DateTime? EmptiedAt { get; set; }
}
