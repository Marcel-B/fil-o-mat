namespace Filomat.Api.Domain;

/// <summary>Einstellung, die sich in den Apps ändern lässt (Schlüssel/Wert).</summary>
public class Setting
{
    public required string Key { get; set; }
    public string? Value { get; set; }
}
