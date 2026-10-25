using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Filomat.Api.Printers;

/// <summary>
/// Einmal-Tokens für die Knöpfe einer ntfy-Meldung, mit der das Backend fragt, welche Spule in einem Kopf steckt.
/// Nur im Speicher: nach einem Neustart sind sie abgelaufen, dann wählt man in der App.
/// </summary>
public class SpoolChoices(TimeProvider time)
{
    public record Choice(string SpoolId, string PrinterId, int Tool, DateTimeOffset Expires);

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<string, Choice> _choices = new();

    public string Add(string spoolId, string printerId, int tool)
    {
        var now = time.GetUtcNow();
        foreach (var old in _choices.Where(c => c.Value.Expires < now).ToList()) _choices.TryRemove(old.Key, out _);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _choices[token] = new Choice(spoolId, printerId, tool, now + Lifetime);
        return token;
    }

    /// <summary>Löst das Token ein; die anderen Knöpfe derselben Frage verfallen mit.</summary>
    public Choice? Take(string token)
    {
        if (!_choices.TryRemove(token, out var choice) || choice.Expires < time.GetUtcNow()) return null;
        foreach (var other in _choices.Where(c => c.Value.PrinterId == choice.PrinterId && c.Value.Tool == choice.Tool).ToList())
            _choices.TryRemove(other.Key, out _);
        return choice;
    }
}

/// <summary>
/// Adresse, unter der die Apps das Backend erreichen; daraus entstehen die Links der Knöpfe in ntfy-Meldungen.
/// <c>Filomat:PublicUrl</c> legt sie fest, sonst gilt die Adresse des letzten Aufrufs einer App.
/// </summary>
public class BackendAddress(IConfiguration configuration)
{
    private string? _seen;

    public string? Url => configuration["Filomat:PublicUrl"] is { Length: > 0 } configured ? configured.TrimEnd('/') : _seen;

    public void Remember(HttpRequest request)
    {
        var proto = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        if (!string.IsNullOrEmpty(host)) _seen = $"{proto}://{host}{request.PathBase}";
    }
}
