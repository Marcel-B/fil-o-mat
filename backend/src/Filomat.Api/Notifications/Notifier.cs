using System.Net.Http.Headers;
using System.Text;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Monitoring;
using Filomat.Api.Printers;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Notifications;

/// <summary>Ziel für Meldungen; in den Apps einstellbar, sonst aus <c>Ntfy:Url</c> und <c>Ntfy:Token</c>.</summary>
/// <param name="Url">Adresse des Themas, z. B. <c>https://ntfy.sh/filomat-abc123</c>.</param>
/// <param name="FromApp">In der App eingestellt (sonst aus der Server-Konfiguration).</param>
/// <param name="Language">Sprache der Meldungen, <c>de</c> oder <c>en</c>.</param>
public record NtfySettings(string? Url, string? Token, bool FromApp, string Language = L.German)
{
    public bool Configured => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}

/// <summary>Knopf in einer Meldung, der beim Antippen <paramref name="Url"/> per POST aufruft.</summary>
public record NtfyAction(string Label, string Url);

/// <summary>Schickt Meldungen über ntfy (https://ntfy.sh oder selbst gehostet), z. B. wenn ein Druck fertig ist.</summary>
public class Notifier(FilomatDbContext db, IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<Notifier> logger)
{
    public const string HttpClientName = "ntfy";
    public const string UrlKey = "ntfy.url";
    public const string TokenKey = "ntfy.token";
    public const string LanguageKey = "ntfy.language";

    public async Task<NtfySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.Settings.Where(s => s.Key == UrlKey || s.Key == TokenKey || s.Key == LanguageKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        var language = L.Normalize(stored.GetValueOrDefault(LanguageKey) ?? configuration["Ntfy:Language"]);
        if (stored.GetValueOrDefault(UrlKey) is { Length: > 0 } url)
            return new NtfySettings(url, stored.GetValueOrDefault(TokenKey), true, language);
        return new NtfySettings(Clean(configuration["Ntfy:Url"]), Clean(configuration["Ntfy:Token"]), false, language);
    }

    /// <summary>Sprache der Meldungen; Texte dafür entstehen innerhalb von <c>using var _ = L.Use(await notifier.LanguageAsync())</c>.</summary>
    public async Task<string> LanguageAsync(CancellationToken cancellationToken = default) =>
        L.Normalize((await db.Settings.FindAsync([LanguageKey], cancellationToken))?.Value ?? configuration["Ntfy:Language"]);

    /// <summary>
    /// Speichert das Ziel; eine leere Adresse fällt auf die Server-Konfiguration zurück. <c>token = null</c> behält den alten,
    /// <c>language = null</c> die bisherige Sprache.
    /// </summary>
    public async Task SaveSettingsAsync(string? url, string? token, string? language = null, CancellationToken cancellationToken = default)
    {
        await SetAsync(UrlKey, Clean(url), cancellationToken);
        if (language is not null) await SetAsync(LanguageKey, L.Normalize(language), cancellationToken);
        if (token is not null || Clean(url) is null) await SetAsync(TokenKey, Clean(token), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var setting = await db.Settings.FindAsync([key], cancellationToken);
        if (value is null)
        {
            if (setting is not null) db.Settings.Remove(setting);
            return;
        }
        if (setting is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else setting.Value = value;
    }

    /// <summary>Schickt eine Meldung, falls ein Ziel eingestellt ist. Fehler landen nur im Log, außer <paramref name="throwOnError"/>.</summary>
    /// <param name="tags">ntfy-Tags, z. B. <c>white_check_mark</c> (wird als Emoji gezeigt).</param>
    /// <param name="priority">1 (min) bis 5 (dringend), 3 ist normal.</param>
    /// <param name="actions">Höchstens drei Knöpfe (Grenze von ntfy).</param>
    /// <param name="image">Holt ein Bild als Anhang (nur, wenn ein Ziel eingestellt ist); nimmt der Server keine Anhänge an, geht die Meldung ohne raus.</param>
    public async Task<bool> SendAsync(string title, string message, string? tags = null, int priority = 3,
        bool throwOnError = false, CancellationToken cancellationToken = default, IReadOnlyList<NtfyAction>? actions = null,
        Func<CancellationToken, Task<Thumbnail?>>? image = null)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.Configured) return false;
        try
        {
            if (image is not null && await image(cancellationToken) is { } attachment)
            {
                try
                {
                    await PostAsync(settings, title, message, tags, priority, actions, attachment, cancellationToken);
                    FilomatMetrics.Notifications.WithLabels("ok").Inc();
                    return true;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is not null)
                {
                    // Z. B. Anhänge auf dem eigenen Server abgeschaltet oder Bild zu groß
                    logger.LogInformation(ex, "ntfy nimmt das Bild nicht an, Meldung geht ohne raus");
                }
            }
            await PostAsync(settings, title, message, tags, priority, actions, null, cancellationToken);
            FilomatMetrics.Notifications.WithLabels("ok").Inc();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Meldung über ntfy fehlgeschlagen");
            FilomatMetrics.Notifications.WithLabels("failed").Inc();
            if (throwOnError) throw;
            return false;
        }
    }

    /// <summary>Mit Bild per PUT, das Bild ist dann der Inhalt und der Text steht in der Kopfzeile <c>Message</c>.</summary>
    private async Task PostAsync(NtfySettings settings, string title, string message, string? tags, int priority,
        IReadOnlyList<NtfyAction>? actions, Thumbnail? image, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(image is null ? HttpMethod.Post : HttpMethod.Put, settings.Url);
        if (image is null)
            request.Content = new StringContent(message, Encoding.UTF8, "text/plain");
        else
        {
            request.Content = new ByteArrayContent(image.Data);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            request.Headers.TryAddWithoutValidation("Filename", image.ContentType == "image/png" ? "bild.png" : "bild.jpg");
            request.Headers.TryAddWithoutValidation("Message", Encode(message));
        }
        // Kopfzeilen sind ASCII; ntfy versteht RFC 2047 für Umlaute
        request.Headers.TryAddWithoutValidation("Title", Encode(title));
        if (tags is not null) request.Headers.TryAddWithoutValidation("Tags", tags);
        if (priority != 3) request.Headers.TryAddWithoutValidation("Priority", priority.ToString());
        if (actions is { Count: > 0 })
            request.Headers.TryAddWithoutValidation("Actions",
                string.Join("; ", actions.Take(3).Select(a => $"http, {a.Label}, {a.Url}, method=POST, clear=true")));
        if (settings.Token is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string Encode(string value) => $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
