using System.Net;
using System.Net.Http.Json;
using Filomat.Api.Domain;
using Filomat.Api.Endpoints;

namespace Filomat.Api.Tests;

public class ApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ApiTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task CreatesStandardContainers()
    {
        var containers = await _client.GetFromJsonAsync<List<Container>>("/api/containers");
        Assert.Equal([Container.PrinterId, Container.LooseId], containers!.Select(c => c.Id).Order());
        Assert.All(containers!, c => Assert.True(c.IsStandard));
    }

    [Fact]
    public async Task CreatesContainerAndFindsItByTag()
    {
        var response = await _client.PostAsJsonAsync("/api/containers",
            new ContainerRequest("box-1", "04A1B2C3", "Box 1", "Regal oben", 4));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var found = await _client.GetFromJsonAsync<Container>("/api/containers/by-tag/04A1B2C3");
        Assert.Equal("box-1", found!.Id);
        Assert.Equal(4, found.Capacity);
    }

    [Fact]
    public async Task RejectsDuplicateTags()
    {
        await _client.PostAsJsonAsync("/api/containers", new ContainerRequest(null, "TAG1", "Box", null, null));

        var container = await _client.PostAsJsonAsync("/api/containers", new ContainerRequest(null, "TAG1", "Box 2", null, null));
        var item = await _client.PostAsJsonAsync("/api/items", new ItemRequest(null, "TAG1", "PLA", null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, container.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, item.StatusCode);
    }

    [Fact]
    public async Task StoresFilamentDataInOpenSpoolFormat()
    {
        var data = new FilamentData { Protocol = "openspool", Type = "PETG", Brand = "Sunlu", ColorHex = "FF0000", MinTemp = "230" };
        var response = await _client.PostAsJsonAsync("/api/items",
            new ItemRequest("spool-1", "04FFEE", "Sunlu PETG", null, Container.LooseId, data));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await _client.GetStringAsync("/api/items/spool-1");
        Assert.Contains("\"color_hex\":\"FF0000\"", json);
        Assert.Contains("\"min_temp\":\"230\"", json);

        var inLoose = await _client.GetFromJsonAsync<List<Item>>($"/api/containers/{Container.LooseId}/items");
        Assert.Equal("PETG", Assert.Single(inLoose!).ItemData!.Type);
    }

    [Fact]
    public async Task MovesItemsBetweenContainersAndOut()
    {
        await _client.PostAsJsonAsync("/api/items", new ItemRequest("spool-1", "04FFEE", "PLA", null, Container.LooseId, null));

        var moved = await _client.PostAsJsonAsync("/api/items/spool-1/move", new MoveItemRequest(Container.PrinterId));
        Assert.Equal(Container.PrinterId, (await moved.Content.ReadFromJsonAsync<Item>())!.ContainerId);

        var removed = await _client.PostAsJsonAsync("/api/items/spool-1/move", new MoveItemRequest(null));
        Assert.Null((await removed.Content.ReadFromJsonAsync<Item>())!.ContainerId);

        var invalid = await _client.PostAsJsonAsync("/api/items/spool-1/move", new MoveItemRequest("nope"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task DeletingContainerMovesItemsToLoose()
    {
        await _client.PostAsJsonAsync("/api/containers", new ContainerRequest("box-1", "TAG1", "Box", null, null));
        await _client.PostAsJsonAsync("/api/items", new ItemRequest("spool-1", "TAG2", "PLA", null, "box-1", null));

        var response = await _client.DeleteAsync("/api/containers/box-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var item = await _client.GetFromJsonAsync<Item>("/api/items/spool-1");
        Assert.Equal(Container.LooseId, item!.ContainerId);
    }

    [Fact]
    public async Task StandardContainersCannotBeDeleted()
    {
        var response = await _client.DeleteAsync($"/api/containers/{Container.LooseId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatesItemData()
    {
        await _client.PostAsJsonAsync("/api/items", new ItemRequest("spool-1", "TAG2", "PLA", null, null, null));

        var response = await _client.PutAsJsonAsync("/api/items/spool-1",
            new ItemUpdateRequest("Prusament PLA", "fast leer", new FilamentData { Type = "PLA", Brand = "Prusament" }));

        var item = await response.Content.ReadFromJsonAsync<Item>();
        Assert.Equal("Prusament", item!.ItemData!.Brand);
        Assert.Equal("fast leer", item.Description);
    }
}

public class ApiKeyTests : IDisposable
{
    private readonly ApiFactory _factory = new() { ApiKey = "geheim" };

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task RequiresApiKeyWhenConfigured()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/containers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", "geheim");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/containers")).StatusCode);
    }
}
