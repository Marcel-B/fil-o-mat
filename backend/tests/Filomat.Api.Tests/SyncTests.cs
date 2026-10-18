using System.Net.Http.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;

namespace Filomat.Api.Tests;

public class SyncTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public SyncTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    private static Snapshot AndroidData() => new(
        [
            new SnapshotContainer(Container.LooseId, "STANDARD_LOSE", "Lose (umbenannt)", null, null, 1, true),
            new SnapshotContainer("box-1", "04AABBCC", "Box 1", "Regal", 6, 1_700_000_000_000),
        ],
        [
            new SnapshotItem("spool-1", "04112233", "PLA Rot", "", 1_700_000_100_000, "box-1",
                new FilamentData { Type = "PLA", ColorHex = "FF0000" }),
            new SnapshotItem("spool-2", "04445566", "PETG", "", 1_700_000_200_000, "gibt-es-nicht", null),
        ]);

    [Fact]
    public async Task ImportsAndroidDataIdempotently()
    {
        var first = await (await _client.PostAsJsonAsync("/api/import", AndroidData())).Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(1, first!.Containers);
        Assert.Equal(2, first.Items);
        Assert.Empty(first.Skipped);

        await _client.PostAsJsonAsync("/api/import", AndroidData());
        var snapshot = await _client.GetFromJsonAsync<Snapshot>("/api/snapshot");

        Assert.Equal(3, snapshot!.Containers.Count);
        Assert.Equal("Lose", snapshot.Containers.Single(c => c.Id == Container.LooseId).Name);
        var box = snapshot.Containers.Single(c => c.Id == "box-1");
        Assert.Equal(1_700_000_000_000, box.CreatedAt);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal("box-1", snapshot.Items.Single(i => i.Id == "spool-1").ContainerId);
        Assert.Equal("FF0000", snapshot.Items.Single(i => i.Id == "spool-1").ItemData!.ColorHex);
        Assert.Null(snapshot.Items.Single(i => i.Id == "spool-2").ContainerId);
    }

    [Fact]
    public async Task SkipsEntriesWhoseTagBelongsToSomethingElse()
    {
        await _client.PostAsJsonAsync("/api/containers", new ContainerRequest("other", "04AABBCC", "Andere Box", null, null));

        var result = await (await _client.PostAsJsonAsync("/api/import", AndroidData())).Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(0, result!.Containers);
        Assert.Single(result.Skipped);
        // Spule ohne gültigen Container landet ohne Container
        Assert.Null((await _client.GetFromJsonAsync<Item>("/api/items/spool-1"))!.ContainerId);
    }
}
