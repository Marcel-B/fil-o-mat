using Microsoft.AspNetCore.Mvc.Testing;

namespace Filomat.Api.Tests;

/// <summary>Startet die API mit einer eigenen SQLite-Datei pro Testklasse.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"filomat-test-{Guid.NewGuid()}.db");

    public string? ApiKey { get; init; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Filomat", $"Data Source={_dbPath}");
        builder.UseSetting("Filomat:ApiKey", ApiKey ?? "");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
