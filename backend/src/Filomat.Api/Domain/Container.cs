namespace Filomat.Api.Domain;

/// <summary>Lagerort (Box, Regal, Drucker …) mit RFID-Tag.</summary>
public class Container
{
    public const string PrinterId = "standard_drucker";
    public const string LooseId = "standard_lose";

    public required string Id { get; set; }
    public required string RfidTag { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int? Capacity { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsStandard { get; set; }

    public static IReadOnlyList<Container> StandardContainers(DateTime createdAt) =>
    [
        new()
        {
            Id = PrinterId,
            RfidTag = "STANDARD_DRUCKER",
            Name = "Drucker",
            Description = "Standard-Container für Drucker",
            IsStandard = true,
            CreatedAt = createdAt,
        },
        new()
        {
            Id = LooseId,
            RfidTag = "STANDARD_LOSE",
            Name = "Lose",
            Description = "Standard-Container für lose Items",
            IsStandard = true,
            CreatedAt = createdAt,
        },
    ];
}
