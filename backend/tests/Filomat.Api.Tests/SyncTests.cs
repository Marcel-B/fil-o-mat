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

    [Fact]
    public async Task ItemHasExtraTagsThatAreFoundAndKept()
    {
        var plates = new Snapshot([], [new SnapshotItem("filofa", "04000001", "FiloFa PLA", "", 1, null, null,
            ExtraTags: ["04000002", "04000003", "04000001", " "])]);
        Assert.Empty((await (await _client.PostAsJsonAsync("/api/import", plates)).Content.ReadFromJsonAsync<ImportResult>())!.Skipped);

        Assert.Equal("filofa", (await _client.GetFromJsonAsync<Item>("/api/items/by-tag/04000003"))!.Id);
        Assert.Equal(["04000002", "04000003"], (await _client.GetFromJsonAsync<Snapshot>("/api/snapshot"))!.Items.Single().ExtraTags);

        // Ältere App ohne das Feld lässt die Tags stehen
        await _client.PostAsJsonAsync("/api/import", new Snapshot([], [new SnapshotItem("filofa", "04000001", "FiloFa", "", 1, null, null)]));
        Assert.Equal(2, (await _client.GetFromJsonAsync<Item>("/api/items/filofa"))!.ExtraTags.Count);

        // Ein weiteres Tag eines anderen Filaments wird nicht übernommen
        var other = new Snapshot([], [new SnapshotItem("other", "04000009", "Anderes", "", 1, null, null, ExtraTags: ["04000002"])]);
        var result = await (await _client.PostAsJsonAsync("/api/import", other)).Content.ReadFromJsonAsync<ImportResult>();
        Assert.Single(result!.Skipped);
        Assert.Empty((await _client.GetFromJsonAsync<Item>("/api/items/other"))!.ExtraTags);
    }

    [Fact]
    public async Task MergingMovesSpoolsStockAndTagsAndRenumbersCollisions()
    {
        var both = new Snapshot([], [
            new SnapshotItem("neu", "04000001", "SUNLU PLA matte", "", 1, null, null, SpoolsInStock: 3),
            new SnapshotItem("alt", "04000002", "SUNLU PLA matte", "", 2, Container.LooseId, null, SpoolsInStock: 2, ExtraTags: ["04000003"]),
        ]);
        await _client.PostAsJsonAsync("/api/import", both);
        await _client.PostAsJsonAsync("/api/items/neu/spools", new OpenSpoolRequest(500));
        await _client.PostAsJsonAsync("/api/items/alt/spools", new OpenSpoolRequest(100));
        await _client.PostAsJsonAsync("/api/items/alt/spools", new OpenSpoolRequest(200));

        var response = await _client.PostAsJsonAsync("/api/items/neu/merge", new MergeItemRequest("alt"));
        var result = await response.Content.ReadFromJsonAsync<MergeItemResult>();

        Assert.Equal([new RenumberedSpool(1, 3)], result!.Renumbered);
        var item = result.Item;
        Assert.Equal((2, Container.LooseId), (item.SpoolsInStock!.Value, item.ContainerId));
        Assert.Equal(["04000002", "04000003"], item.ExtraTags);
        var snapshot = await _client.GetFromJsonAsync<Snapshot>("/api/snapshot");
        Assert.Equal("neu", snapshot!.Items.Single().Id);
        Assert.Equal([(1, 500.0), (2, 200.0), (3, 100.0)],
            snapshot.Spools!.OrderBy(s => s.Number).Select(s => (s.Number, s.RemainingWeight!.Value)).ToList());
        Assert.Equal("neu", (await _client.GetFromJsonAsync<Item>("/api/items/by-tag/04000003"))!.Id);
    }

    [Fact]
    public async Task KeepsTransmissionDistanceWhenAppDoesNotSendIt()
    {
        var withTd = new Snapshot([], [new SnapshotItem("pla", "04112233", "PLA", "", 1, null, null, TransmissionDistance: 4.2)]);
        await _client.PostAsJsonAsync("/api/import", withTd);
        Assert.Equal(4.2, (await _client.GetFromJsonAsync<Snapshot>("/api/snapshot"))!.Items.Single().TransmissionDistance);

        // Ältere App ohne das Feld
        await _client.PostAsJsonAsync("/api/import", new Snapshot([], [new SnapshotItem("pla", "04112233", "PLA neu", "", 1, null, null)]));
        var item = await _client.GetFromJsonAsync<Item>("/api/items/pla");
        Assert.Equal(("PLA neu", 4.2), (item!.Name, item.TransmissionDistance!.Value));

        var negative = await _client.PutAsJsonAsync("/api/items/pla", new ItemUpdateRequest("PLA", null, null, TransmissionDistance: -1));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, negative.StatusCode);
    }
}
