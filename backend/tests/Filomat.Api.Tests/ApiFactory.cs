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

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Filomat", $"Data Source={_dbPath}");
        builder.UseSetting("Filomat:ApiKey", ApiKey ?? "");
        // Kein automatischer Abgleich in Tests
        builder.UseSetting("Filomat:PrinterSyncInterval", "00:00:00");
        if (PrinterHandler is { } handler)
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<MoonrakerAdapter>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<SnapmakerU1Adapter>().ConfigurePrimaryHttpMessageHandler(() => handler);
            });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
