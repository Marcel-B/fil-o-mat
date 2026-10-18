using System.Net.Http.Json;
using System.Text.Json;
using Filomat.Api.Catalog;
using Filomat.Api.Endpoints;
using Filomat.Api.Domain;

namespace Filomat.Api.Tests;

public class SpoolWeightTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public SpoolWeightTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void TakesMostCommonSpoolWeightPerManufacturer()
    {
        using var json = JsonDocument.Parse("""
            [{"manufacturer":"Sunlu","material":"PLA","spool_weight":160},
             {"manufacturer":"Sunlu","material":"PETG","spool_weight":160},
             {"manufacturer":"Sunlu","material":"PLA","spool_weight":250},
             {"manufacturer":"Overture","material":"TPU","spool_weight":200},
             {"manufacturer":"Unknown","material":"PLA"},
             {"manufacturer":"Broken","spool_weight":"x"}]
            """);
        var weights = SpoolWeightCatalog.Parse(json.RootElement);

        Assert.Equal(160, weights["sunlu"]);
        Assert.Equal(200, weights["Overture"]);
        Assert.False(weights.ContainsKey("Unknown"));
        Assert.False(weights.ContainsKey("Broken"));
    }

    [Fact]
    public async Task EndpointReturnsEmptyListWithoutInternet()
    {
        var weights = await _client.GetFromJsonAsync<Dictionary<string, double>>("/api/spool-weights");
        Assert.Empty(weights!);
    }

    [Fact]
    public async Task StoresSpoolWeight()
    {
        var created = await (await _client.PostAsJsonAsync("/api/items",
            new ItemRequest(null, "T1", "PLA", null, null, null, 1000, 160))).Content.ReadFromJsonAsync<Item>();
        Assert.Equal(160, created!.SpoolWeight);

        var snapshot = await _client.GetFromJsonAsync<Snapshot>("/api/snapshot");
        Assert.Equal(160, snapshot!.Items.Single().SpoolWeight);
    }
}
