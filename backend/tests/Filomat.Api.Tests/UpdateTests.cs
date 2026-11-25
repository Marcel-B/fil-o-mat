using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Endpoints;

namespace Filomat.Api.Tests;

public class UpdateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("filomat-update-").FullName;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public UpdateTests()
    {
        _factory = new ApiFactory { UpdateDirectory = _dir };
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private void WriteStatus(string state = "idle", string commits = """[{"sha":"bbb","subject":"Neue Funktion","date":1760820000}]""") =>
        File.WriteAllText(Path.Combine(_dir, "status.json"), $$"""
            {"branch":"main","current":"aaa","currentDate":1760800000,"latest":"bbb",
             "commits":{{commits}},"checkedAt":1760830000,"state":"{{state}}","message":null}
            """);

    [Fact]
    public async Task ReportsUnsupportedWithoutUpdater()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<UpdateStatus>("/api/update");
        Assert.False(status!.Supported);

        var response = await client.PostAsync("/api/update", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReportsApiLevelAndRunningCommit()
    {
        var bare = await _client.GetFromJsonAsync<VersionResponse>("/api/version");
        Assert.Equal((ApiVersion.Level, ApiVersion.MinAppLevel, (string?)null), (bare!.ApiLevel, bare.MinAppLevel, bare.Commit));

        WriteStatus();
        var version = await _client.GetFromJsonAsync<VersionResponse>("/api/version");
        Assert.Equal("aaa", version!.Commit);
    }

    [Fact]
    public async Task ReadsStatusWrittenByUpdater()
    {
        WriteStatus();

        var status = await _client.GetFromJsonAsync<UpdateStatus>("/api/update");

        Assert.True(status!.Supported);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("aaa", status.CurrentVersion);
        Assert.Equal("bbb", status.LatestVersion);
        Assert.Equal(1760830000000, status.CheckedAt);
        Assert.Equal("Neue Funktion", Assert.Single(status.Commits).Subject);
        Assert.Equal("idle", status.State);
    }

    [Fact]
    public async Task ApplyWritesRequestForUpdater()
    {
        WriteStatus();

        var response = await _client.PostAsync("/api/update", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("apply", File.ReadAllText(Path.Combine(_dir, "request")).Trim());
        Assert.Equal("requested", (await response.Content.ReadFromJsonAsync<UpdateStatus>())!.State);

        // Eine Prüfung ersetzt den wartenden Auftrag nicht
        await _client.PostAsync("/api/update/check", null);
        Assert.Equal("apply", File.ReadAllText(Path.Combine(_dir, "request")).Trim());
    }

    [Fact]
    public async Task RefusesApplyWithoutNewVersionOrWhileRunning()
    {
        WriteStatus(commits: "[]");
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync("/api/update", null)).StatusCode);

        WriteStatus(state: "running");
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync("/api/update", null)).StatusCode);
        Assert.False(File.Exists(Path.Combine(_dir, "request")));
    }

    [Fact]
    public async Task CheckWritesRequest()
    {
        WriteStatus(commits: "[]");

        var response = await _client.PostAsync("/api/update/check", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("check", File.ReadAllText(Path.Combine(_dir, "request")).Trim());
    }
}
