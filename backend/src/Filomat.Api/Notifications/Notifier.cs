using System.Net.Http.Headers;
using System.Text;
using Filomat.Api.Data;
using Filomat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filomat.Api.Notifications;

/// <summary>Ziel für Meldungen; in den Apps einstellbar, sonst aus <c>Ntfy:Url</c> und <c>Ntfy:Token</c>.</summary>
/// <param name="Url">Adresse des Themas, z. B. <c>https://ntfy.sh/filomat-abc123</c>.</param>
/// <param name="FromApp">In der App eingestellt (sonst aus der Server-Konfiguration).</param>
public record NtfySettings(string? Url, string? Token, bool FromApp)
{
    public bool Configured => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}

/// <summary>Schickt Meldungen über ntfy (https://ntfy.sh oder selbst gehostet), z. B. wenn ein Druck fertig ist.</summary>
public class Notifier(FilomatDbContext db, IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<Notifier> logger)
{
    public const string HttpClientName = "ntfy";
    public const string UrlKey = "ntfy.url";
    public const string TokenKey = "ntfy.token";

    public async Task<NtfySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.Settings.Where(s => s.Key == UrlKey || s.Key == TokenKey).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        if (stored.GetValueOrDefault(UrlKey) is { Length: > 0 } url)
            return new NtfySettings(url, stored.GetValueOrDefault(TokenKey), true);
        return new NtfySettings(Clean(configuration["Ntfy:Url"]), Clean(configuration["Ntfy:Token"]), false);
    }

    /// <summary>Speichert das Ziel; eine leere Adresse fällt auf die Server-Konfiguration zurück. <c>token = null</c> behält den alten.</summary>
    public async Task SaveSettingsAsync(string? url, string? token, CancellationToken cancellationToken = default)
    {
        await SetAsync(UrlKey, Clean(url), cancellationToken);
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
    public async Task<bool> SendAsync(string title, string message, string? tags = null, int priority = 3,
        bool throwOnError = false, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.Configured) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
            {
                Content = new StringContent(message, Encoding.UTF8, "text/plain"),
            };
            // Kopfzeilen sind ASCII; ntfy versteht RFC 2047 für Umlaute im Titel
            request.Headers.TryAddWithoutValidation("Title", $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(title))}?=");
            if (tags is not null) request.Headers.TryAddWithoutValidation("Tags", tags);
            if (priority != 3) request.Headers.TryAddWithoutValidation("Priority", priority.ToString());
            if (settings.Token is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Meldung über ntfy fehlgeschlagen");
            if (throwOnError) throw;
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
