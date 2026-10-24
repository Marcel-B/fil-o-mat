using Filomat.Api.Printers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Filomat.Api.Tests;

/// <summary>Startet die API mit einer eigenen SQLite-Datei pro Testklasse.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"filomat-test-{Guid.NewGuid()}.db");

    public string? ApiKey { get; init; }

    /// <summary>Ersetzt die HTTP-Verbindung zu den Druckern.</summary>
    public HttpMessageHandler? PrinterHandler { get; init; }

    /// <summary>Ersetzt die MQTT-Abfrage von Anycubic-Druckern.</summary>
    public IAnycubicLan? AnycubicLan { get; init; }

    /// <summary>Ersetzt MQTT und FTPS zu Bambu-Druckern.</summary>
    public IBambuLan? BambuLan { get; init; }

    /// <summary>Ersetzt die Nextcloud; ohne bleibt die Sicherung aus.</summary>
    public HttpMessageHandler? NextcloudHandler { get; init; }

    /// <summary>Ersetzt ntfy; ohne ist keine Adresse für Meldungen eingestellt.</summary>
    public HttpMessageHandler? NtfyHandler { get; init; }

    /// <summary>Ordner, den sich API und Updater teilen (status.json, request).</summary>
    public string? UpdateDirectory { get; init; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Filomat", $"Data Source={_dbPath}");
        builder.UseSetting("Filomat:ApiKey", ApiKey ?? "");
        builder.UseSetting("Filomat:UpdateDirectory", UpdateDirectory ?? "");
        // Kein automatischer Abgleich in Tests
        builder.UseSetting("Filomat:PrinterSyncInterval", "00:00:00");
        // Kein Zugriff aufs Internet in Tests
        builder.UseSetting("Filomat:SpoolWeightCatalogUrl", "http://127.0.0.1:9/filaments.json");
        builder.UseSetting("Backup:At", "");
        if (NextcloudHandler is { } nextcloud)
        {
            builder.UseSetting("Backup:WebDavUrl", "https://cloud.test/remote.php/dav/files/marcel/fil-o-mat");
            builder.UseSetting("Backup:Username", "marcel");
            builder.UseSetting("Backup:AppPassword", "geheim");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(Filomat.Api.Backup.WebDavClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => nextcloud));
        }
        if (NtfyHandler is { } ntfy)
        {
            builder.UseSetting("Ntfy:Url", "https://ntfy.test/filomat");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(Filomat.Api.Notifications.Notifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => ntfy));
        }
        if (PrinterHandler is { } handler)
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<MoonrakerAdapter>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<SnapmakerU1Adapter>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<PrusaLinkAdapter>().ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        if (AnycubicLan is { } lan)
            builder.ConfigureTestServices(services => services.AddSingleton(lan));
        if (BambuLan is { } bambu)
            builder.ConfigureTestServices(services => services.AddSingleton(bambu));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
