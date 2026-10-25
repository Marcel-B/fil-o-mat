using System.Collections.Concurrent;
using System.Security.Cryptography;
using Filomat.Api.Domain;
using Filomat.Api.Notifications;

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

/// <summary>Fragt per ntfy, welche Spule in einem Kopf steckt, wenn ein Plättchen erkannt wurde und mehrere Spulen lose liegen.</summary>
public class SpoolQuestion(SpoolStock spools, Notifier notifier, PrinterStatusCache cache, SpoolChoices choices, BackendAddress address)
{
    /// <summary>Fragt per ntfy, welche der losen Spulen in diesem Kopf steckt, mit einem Knopf pro Spule (höchstens drei).</summary>
    public async Task AskAsync(Printer printer, int tool, Item item, CancellationToken cancellationToken)
    {
        var loose = await spools.LooseAsync(item, cancellationToken);
        if (loose.Count < 2) return;
        // Einmal pro Kopf, Sorte und Auswahl, nicht bei jedem Abgleich
        if (!cache.FirstTime($"unclear|{printer.Id}|{tool}|{item.Id}|{string.Join(",", loose.Select(s => s.Id))}")) return;

        var place = PrinterInsights.PlaceName(printer.Type, PrinterLive.ToolCount(printer), tool);
        var where = place.Length > 0 ? $"{printer.Name} {place}" : printer.Name;
        static string Label(Spool s) => s.RemainingWeight is { } grams ? $"#{s.Number} ({grams:0} g)" : $"#{s.Number}";
        var actions = address.Url is { } url
            ? loose.Take(3).Select(s => new NtfyAction(Label(s), $"{url}/choices/{choices.Add(s.Id, printer.Id, tool)}")).ToList()
            : new List<NtfyAction>();
        var message = $"{item.Name} erkannt, lose liegen {string.Join(", ", loose.Select(Label))}. "
            + (actions.Count == loose.Count ? "Tippe auf die Spule, die drin steckt." : "Wählen kannst du in der App beim Drucker.");
        await notifier.SendAsync($"{where}: Welche Spule?", message, "question", cancellationToken: cancellationToken, actions: actions);
    }
}
