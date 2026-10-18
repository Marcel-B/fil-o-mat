using System.Globalization;

namespace Filomat.Api.Backup;

/// <summary>
/// Nächtliche Sicherung in eine Nextcloud (oder einen anderen WebDAV-Server), Abschnitt <c>Backup</c>. Ohne Adresse,
/// Benutzer und Passwort ist sie aus.
/// </summary>
/// <remarks>
/// Zugangsdaten gehören nicht ins Repository: Umgebungsvariablen mit den Namen aus module-o-mat und YuE UI
/// (<c>NEXTCLOUD_WEBDAV_URL</c>, <c>NEXTCLOUD_USERNAME</c>, <c>NEXTCLOUD_APP_PASSWORD</c>, im LXC über
/// <c>backend/.env</c>) gehen vor <see cref="EnvFile"/>, die Datei vor den Einstellungen. Das Passwort ist ein
/// App-Passwort (Nextcloud: Einstellungen → Sicherheit → Geräte &amp; Sitzungen), das sich einzeln widerrufen lässt.
/// </remarks>
public sealed class BackupOptions
{
    public const string Section = "Backup";

    public const string UrlVariable = "NEXTCLOUD_WEBDAV_URL";
    public const string UserVariable = "NEXTCLOUD_USERNAME";
    public const string PasswordVariable = "NEXTCLOUD_APP_PASSWORD";

    /// <summary>Optionale Datei mit <c>KEY=value</c>-Zeilen; wird bei jeder Sicherung neu gelesen.</summary>
    public string? EnvFile { get; set; }

    /// <summary>
    /// Ersatz für <c>NEXTCLOUD_WEBDAV_URL</c>: der WebDAV-Ordner der Sicherungen, z. B.
    /// <c>https://cloud.example.de/remote.php/dav/files/marcel/Backups/fil-o-mat</c>. Der Ordner selbst wird angelegt,
    /// der darüber muss existieren.
    /// </summary>
    public string? WebDavUrl { get; set; }

    public string? Username { get; set; }

    public string? AppPassword { get; set; }

    /// <summary>Startzeit der nächtlichen Sicherung in <see cref="TimeZone"/>; leer für nur manuelle Sicherungen.</summary>
    public string? At { get; set; } = "03:00";

    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>Eine fehlgeschlagene nächtliche Sicherung wird nach dieser Zeit wiederholt, nicht jede Minute.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public Uri? BaseUri =>
        Uri.TryCreate(Value(UrlVariable, WebDavUrl) is { } url ? url.TrimEnd('/') + "/" : null, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" ? uri : null;

    public string? ResolvedUsername => Value(UserVariable, Username);

    public string? ResolvedPassword => Value(PasswordVariable, AppPassword);

    public bool Configured => BaseUri is not null && ResolvedUsername is not null && ResolvedPassword is not null;

    public TimeOnly? DailyAt => TimeOnly.TryParse(At, CultureInfo.InvariantCulture, out var at) ? at : null;

    public TimeZoneInfo Zone
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Local;
            }
        }
    }

    private string? Value(string name, string? setting) =>
        Clean(Environment.GetEnvironmentVariable(name)) ?? Clean(ReadEnvFile().GetValueOrDefault(name)) ?? Clean(setting);

    private Dictionary<string, string> ReadEnvFile()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(EnvFile)) return values;
        var path = EnvFile.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), EnvFile[2..])
            : EnvFile;
        try
        {
            if (!File.Exists(path)) return values;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
                if (line.Length == 0 || line[0] == '#' || line.IndexOf('=') is not (> 0 and var equals)) continue;
                values[line[..equals].Trim()] = line[(equals + 1)..];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return values;
    }

    /// <summary>Getrimmt und ohne umschließende Anführungszeichen; <c>null</c>, wenn leer.</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length >= 2 && (trimmed[0], trimmed[^1]) is ('"', '"') or ('\'', '\''))
            trimmed = trimmed[1..^1].Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }
}
