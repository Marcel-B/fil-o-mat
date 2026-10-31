using System.Globalization;
using System.Text.RegularExpressions;

namespace Filomat.Api.Localization;

/// <summary>
/// Übersetzt Texte für Apps und Meldungen. Der Code bleibt deutsch, der deutsche Text ist der Schlüssel in
/// <see cref="English.Texts"/> (wie in den Apps). Die Sprache kommt aus <see cref="CultureInfo.CurrentUICulture"/>:
/// bei Anfragen aus <c>Accept-Language</c>, bei ntfy-Meldungen aus der Einstellung (<see cref="Use"/>).
/// </summary>
public static class L
{
    public const string German = "de";
    public const string EnglishCode = "en";
    public static readonly string[] Languages = [German, EnglishCode];

    private static readonly Regex Placeholder = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>Vorlagen aus <see cref="English.Messages"/>, um fertige deutsche Meldungen samt eingesetzter Werte zu erkennen.</summary>
    private static readonly (Regex German, string English)[] Templates = English.Messages
        .Where(e => Placeholder.IsMatch(e.Key))
        .OrderByDescending(e => Placeholder.Replace(e.Key, "").Length)
        .Select(e => (new Regex("^" + Placeholder.Replace(Regex.Escape(e.Key).Replace(@"\{", "{"), m => $"(?<p{m.Groups[1].Value}>.*?)") + "$",
            RegexOptions.Singleline | RegexOptions.Compiled), e.Value))
        .ToArray();

    public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == EnglishCode;

    /// <summary>
    /// Übersetzt einen deutschen Text, auch eine gespeicherte Meldung aus <see cref="English.Messages"/> mit schon
    /// eingesetzten Werten. Unbekanntes (z. B. Meldungen des Druckers selbst) bleibt, wie es ist.
    /// </summary>
    public static string T(string german)
    {
        if (!IsEnglish || german.Length == 0) return german;
        if (English.Texts.TryGetValue(german, out var english) || English.Messages.TryGetValue(german, out english)) return english;
        foreach (var (pattern, template) in Templates)
        {
            if (pattern.Match(german) is not { Success: true } match) continue;
            return Placeholder.Replace(template, m => match.Groups["p" + m.Groups[1].Value].Value);
        }
        return german;
    }

    /// <summary>Wie <see cref="string.Format(string, object?[])"/> mit der übersetzten Vorlage.</summary>
    public static string F(string german, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, IsEnglish ? English.Texts.GetValueOrDefault(german, german) : german, args);

    /// <summary>"de" oder "en"; alles andere wird Deutsch.</summary>
    public static string Normalize(string? language) =>
        language?.Trim().ToLowerInvariant() is { Length: >= 2 } code && code[..2] == EnglishCode ? EnglishCode : German;

    /// <summary>Stellt die Sprache bis zum <c>Dispose</c> um, z. B. für ntfy-Meldungen aus dem Hintergrund.</summary>
    public static IDisposable Use(string? language)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Normalize(language));
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
