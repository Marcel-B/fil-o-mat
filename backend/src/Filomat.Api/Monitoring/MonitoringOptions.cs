namespace Filomat.Api.Monitoring;

/// <summary>
/// /metrics für Prometheus und der Versand der Logzeilen an Loki (Abschnitt <c>Monitoring</c>, als Umgebungsvariablen
/// <c>Monitoring__Metrics</c>, <c>Monitoring__Loki__Url</c>, <c>Monitoring__Loki__Host</c>). Ohne Konfiguration bleibt
/// beides aus, wie die Nextcloud-Sicherung.
/// </summary>
public sealed class MonitoringOptions
{
    public bool Metrics { get; init; }

    /// <summary>Basis-URL von Loki (z. B. <c>http://monitoring:3100</c>) oder gleich die volle Push-URL.</summary>
    public string? LokiUrl { get; init; }

    /// <summary>Wert des Labels <c>host</c> in Loki, der Rechner (z. B. <c>proxmox</c>).</summary>
    public string LokiHost { get; init; } = "";

    public bool LokiConfigured => !string.IsNullOrWhiteSpace(LokiUrl);

    public string? LokiPushUrl
    {
        get
        {
            if (!LokiConfigured) return null;
            var url = LokiUrl!.Trim().TrimEnd('/');
            return url.EndsWith("/loki/api/v1/push", StringComparison.Ordinal) ? url : url + "/loki/api/v1/push";
        }
    }

    public static MonitoringOptions Load(IConfiguration configuration) => new()
    {
        Metrics = configuration.GetValue("Monitoring:Metrics", false),
        LokiUrl = Clean(configuration["Monitoring:Loki:Url"]),
        LokiHost = Clean(configuration["Monitoring:Loki:Host"]) ?? Environment.MachineName.ToLowerInvariant(),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
