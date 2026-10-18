using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Backup;
using Filomat.Api.Endpoints;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Filomat.Api.Tests;

public class BackupTests
{
    [Fact]
    public async Task UploadsDatabaseAsWeekdayAndMonthArchive()
    {
        var nextcloud = new FakeNextcloud();
        using var factory = new ApiFactory { NextcloudHandler = nextcloud };
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/containers", new ContainerRequest("box-1", "04A1B2C3", "Box 1", null, null));

        var response = await client.PostAsync("/api/backup", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await factory.Services.GetRequiredService<CloudBackup>().Running;

        Assert.Equal("MKCOL /remote.php/dav/files/marcel/fil-o-mat/", nextcloud.Requests[0]);
        Assert.Equal("Basic bWFyY2VsOmdlaGVpbQ==", nextcloud.Authorization);
        // Die Namen richten sich nach der Zeitzone der Sicherung, nicht nach der des Rechners
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        var archives = nextcloud.Files.Keys.Order().ToList();
        Assert.Equal(2, archives.Count);
        Assert.Contains(archives, a => a == $"/remote.php/dav/files/marcel/fil-o-mat/filomat-{now:yyyy-MM}.zip");
        Assert.Contains(archives, a => a.EndsWith($"filomat-{now.DayOfWeek.ToString()[..3].ToLowerInvariant()}.zip"));

        var copy = Path.Combine(Path.GetTempPath(), $"filomat-restore-{Guid.NewGuid()}.db");
        try
        {
            using (var zip = new ZipArchive(new MemoryStream(nextcloud.Files[archives[0]])))
                Assert.Single(zip.Entries, e => e.FullName == "filomat.db").ExtractToFile(copy);
            using var db = new SqliteConnection($"Data Source={copy};Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT Name FROM Containers WHERE Id = 'box-1'";
            Assert.Equal("Box 1", command.ExecuteScalar());
        }
        finally
        {
            File.Delete(copy);
        }

        var status = await client.GetFromJsonAsync<BackupStatus>("/api/backup");
        Assert.True(status!.Configured);
        Assert.True(status.Last!.Success);
        Assert.Equal(2, status.Last.Archives.Count);
    }

    [Fact]
    public async Task ReportsRefusedPassword()
    {
        using var factory = new ApiFactory { NextcloudHandler = new FakeNextcloud { Status = HttpStatusCode.Unauthorized } };
        var client = factory.CreateClient();

        await client.PostAsync("/api/backup", null);
        await factory.Services.GetRequiredService<CloudBackup>().Running;

        var status = await client.GetFromJsonAsync<BackupStatus>("/api/backup");
        Assert.False(status!.Last!.Success);
        Assert.Contains("401", status.Last.Error);
    }

    [Fact]
    public async Task IsOffWithoutNextcloud()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<BackupStatus>("/api/backup");
        Assert.False(status!.Configured);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/backup", null)).StatusCode);
    }

    private sealed class FakeNextcloud : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.Created;
        public List<string> Requests { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = [];
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add($"{request.Method} {path}");
            Authorization = request.Headers.Authorization?.ToString();
            if (Status == HttpStatusCode.Created && request.Method == HttpMethod.Put)
                Files[path] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }
}
