using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Filomat.Api.Monitoring;
using Filomat.Api.Printers;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Notifications;

/// <summary>
/// Ziel für Meldungen auf einem Onkel-Bummel-Server; in den Apps einstellbar, sonst aus
/// <c>Notifications:Server</c>, <c>Notifications:Topic</c> und <c>Notifications:Token</c>.
/// </summary>
/// <param name="Server">Adresse des Servers, z. B. <c>https://onkel-bummel.tailnet.ts.net</c>.</param>
/// <param name="Topic">Topic, an das die Meldungen gehen, z. B. <c>filomat</c>.</param>
/// <param name="Token">Publish-Token (<c>ob_…</c>), das für das Topic freigeschaltet ist.</param>
/// <param name="FromApp">In der App eingestellt (sonst aus der Server-Konfiguration).</param>
/// <param name="Language">Sprache der Meldungen, <c>de</c> oder <c>en</c>.</param>
public record NotificationSettings(string? Server, string? Topic, string? Token, bool FromApp, string Language = L.German)
{
    public bool Configured => Notifier.IsServerUrl(Server) && Notifier.IsTopic(Topic) && Notifier.IsToken(Token);
}

/// <summary>Knopf in einer Meldung, der beim Antippen <paramref name="Url"/> per POST aufruft.</summary>
public record NotificationAction(string Label, string Url);

/// <summary>Schickt Meldungen an Onkel Bummel (<c>POST /api/v1/messages</c>), z. B. wenn ein Druck fertig ist.</summary>
public partial class Notifier(FilomatDbContext db, IHttpClientFactory httpFactory, IConfiguration configuration, BackendAddress address,
    ILogger<Notifier> logger)
{
    public const string HttpClientName = "onkel-bummel";
    public const string ServerKey = "notifications.server";
    public const string TopicKey = "notifications.topic";
    public const string TokenKey = "notifications.token";
    public const string LanguageKey = "notifications.language";
    /// <summary>Sprache aus der Zeit mit ntfy; gilt, bis eine App neu speichert.</summary>
    private const string OldLanguageKey = "ntfy.language";

    /// <summary>Grenze von Onkel Bummel.</summary>
    public const int MaxActions = 3;

    /// <summary>Versuche pro Meldung, solange der Server nicht erreichbar ist oder 5xx/429 antwortet.</summary>
    private const int Attempts = 3;

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex TopicPattern();

    public static bool IsServerUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static bool IsTopic(string? value) => value is not null && TopicPattern().IsMatch(value);

    public static bool IsToken(string? value) => value is { Length: > 3 } && value.StartsWith("ob_", StringComparison.Ordinal);

    public async Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.Settings.Where(s => s.Key == ServerKey || s.Key == TopicKey || s.Key == TokenKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        var language = await LanguageAsync(cancellationToken);
        if (stored.GetValueOrDefault(ServerKey) is { Length: > 0 } server)
            return new NotificationSettings(server, stored.GetValueOrDefault(TopicKey), stored.GetValueOrDefault(TokenKey), true, language);
        return new NotificationSettings(CleanServer(configuration["Notifications:Server"]), Clean(configuration["Notifications:Topic"]),
            Clean(configuration["Notifications:Token"]), false, language);
    }

    /// <summary>Sprache der Meldungen; Texte dafür entstehen innerhalb von <c>using var _ = L.Use(await notifier.LanguageAsync())</c>.</summary>
    public async Task<string> LanguageAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.Settings.Where(s => s.Key == LanguageKey || s.Key == OldLanguageKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return L.Normalize(stored.GetValueOrDefault(LanguageKey) ?? stored.GetValueOrDefault(OldLanguageKey)
            ?? Clean(configuration["Notifications:Language"]) ?? configuration["Ntfy:Language"]);
    }

    /// <summary>Gespeicherter Token aus der App, z. B. um beim Speichern ohne neuen Token zu prüfen, ob einer da ist.</summary>
    public async Task<string?> StoredTokenAsync(CancellationToken cancellationToken = default) =>
        (await db.Settings.FindAsync([TokenKey], cancellationToken))?.Value;

    /// <summary>
    /// Speichert das Ziel; ein leerer Server fällt auf die Server-Konfiguration zurück (Topic und Token gehen mit).
    /// <c>token = null</c> behält den alten, <c>language = null</c> die bisherige Sprache.
    /// </summary>
    public async Task SaveSettingsAsync(string? server, string? topic, string? token, string? language = null,
        CancellationToken cancellationToken = default)
    {
        server = CleanServer(server);
        await SetAsync(ServerKey, server, cancellationToken);
        await SetAsync(TopicKey, server is null ? null : Clean(topic), cancellationToken);
        if (token is not null || server is null) await SetAsync(TokenKey, server is null ? null : Clean(token), cancellationToken);
        if (language is not null) await SetAsync(LanguageKey, L.Normalize(language), cancellationToken);
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
    /// <param name="tags">Kommagetrennte Tags, z. B. <c>white_check_mark</c> (die Apps zeigen bekannte Namen als Emoji).</param>
    /// <param name="priority">1 (min) bis 5 (dringend), 3 ist normal.</param>
    /// <param name="actions">Höchstens drei Knöpfe (Grenze von Onkel Bummel).</param>
    /// <param name="image">Holt ein Bild als Anhang (nur, wenn ein Ziel eingestellt ist); lehnt der Server es ab, geht die Meldung ohne raus.</param>
    /// <param name="click">Adresse beim Antippen der Meldung; ohne die Druckerseite der Web-Oberfläche, falls die Adresse bekannt ist.</param>
    public async Task<bool> SendAsync(string title, string message, string? tags = null, int priority = 3,
        bool throwOnError = false, CancellationToken cancellationToken = default, IReadOnlyList<NotificationAction>? actions = null,
        Func<CancellationToken, Task<Thumbnail?>>? image = null, string? click = null)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.Configured) return false;
        var notification = new Notification(settings, title, message, tags, Math.Clamp(priority, 1, 5),
            click ?? (address.Url is { } url ? $"{url}/drucker" : null), actions ?? [],
            // Gleicher Schlüssel für alle Versuche: Onkel Bummel legt die Meldung dann nur einmal an
            $"filomat-{Guid.NewGuid():N}");
        try
        {
            if (image is not null && await image(cancellationToken) is { } attachment)
            {
                try
                {
                    await PostAsync(notification, attachment, cancellationToken);
                    FilomatMetrics.Notifications.WithLabels("ok").Inc();
                    return true;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge
                                                          or HttpStatusCode.UnsupportedMediaType)
                {
                    // Bild zu groß, unlesbar oder in einem Format, das der Server nicht nimmt
                    logger.LogInformation(ex, "Onkel Bummel nimmt das Bild nicht an, Meldung geht ohne raus");
                }
            }
            await PostAsync(notification, null, cancellationToken);
            FilomatMetrics.Notifications.WithLabels("ok").Inc();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Meldung an Onkel Bummel fehlgeschlagen");
            FilomatMetrics.Notifications.WithLabels("failed").Inc();
            if (throwOnError) throw;
            return false;
        }
    }

    private record Notification(NotificationSettings Settings, string Title, string Message, string? Tags, int Priority, string? Click,
        IReadOnlyList<NotificationAction> Actions, string IdempotencyKey);

    /// <summary>Wiederholt bei Netzfehlern, Zeitüberschreitung, 5xx und 429; andere Fehler gehen sofort an den Aufrufer.</summary>
    private async Task PostAsync(Notification notification, Thumbnail? image, CancellationToken cancellationToken)
    {
        var delay = configuration.GetValue("Notifications:RetryDelay", TimeSpan.FromSeconds(2));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await PostOnceAsync(notification, image, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < Attempts && !cancellationToken.IsCancellationRequested && ex switch
            {
                HttpRequestException { StatusCode: null } => true,
                HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status == HttpStatusCode.TooManyRequests,
                TaskCanceledException => true,
                _ => false,
            })
            {
                logger.LogInformation(ex, "Onkel Bummel nicht erreichbar, Versuch {Attempt} von {Attempts}", attempt, Attempts);
                await Task.Delay(delay * attempt, cancellationToken);
            }
        }
    }

    private async Task PostOnceAsync(Notification notification, Thumbnail? image, CancellationToken cancellationToken)
    {
        var settings = notification.Settings;
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(settings.Topic!), "topic");
        content.Add(new StringContent(notification.Title), "title");
        content.Add(new StringContent(notification.Message), "message");
        content.Add(new StringContent(notification.Priority.ToString()), "priority");
        if (notification.Tags is { Length: > 0 } tags) content.Add(new StringContent(tags), "tags");
        if (notification.Click is { } click) content.Add(new StringContent(click), "click");
        if (notification.Actions.Count > 0)
            content.Add(new StringContent(JsonSerializer.Serialize(notification.Actions.Take(MaxActions)
                .Select(a => new { label = a.Label, url = a.Url, method = "POST", clear = true }))), "actions");
        if (image is not null)
        {
            var file = new ByteArrayContent(image.Data);
            file.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            content.Add(file, "file", image.ContentType == "image/png" ? "bild.png" : "bild.jpg");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Server!.TrimEnd('/')}/api/v1/messages") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", notification.IdempotencyKey);

        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var problem = await ProblemAsync(response, cancellationToken);
        throw new HttpRequestException($"{(int)response.StatusCode} {problem ?? response.ReasonPhrase}".Trim(), null, response.StatusCode);
    }

    /// <summary>Text aus der Problem-JSON-Antwort (RFC 7807): <c>detail</c>, sonst <c>title</c>.</summary>
    private static async Task<string?> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var name in new[] { "detail", "title" })
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                    return text;
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanServer(string? value) => Clean(value)?.TrimEnd('/');
}
