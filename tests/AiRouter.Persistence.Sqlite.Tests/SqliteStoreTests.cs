using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Persistence.Sqlite.Tests;

public sealed class SqliteStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ai-router-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Provider_round_trip_preserves_configuration_and_secret()
    {
        var store = ProviderStore();
        var provider = new ProviderDefinition(
            Id: "primary",
            Name: "Primary",
            Type: "openai-compatible",
            BaseUrl: "https://example.test/v1",
            ApiKey: "secret",
            Enabled: true,
            Priority: 7,
            Timeout: TimeSpan.FromSeconds(42),
            Models: ["model-a", "model-b"],
            DefaultModel: "model-a",
            DiscoverModels: false,
            ExtraHeaders: new Dictionary<string, string> { ["X-Test"] = "yes" },
            ChatEndpoint: "/chat",
            ResponsesEndpoint: "/responses",
            ModelsEndpoint: "/models",
            SupportsNativeResponses: false);

        await store.UpsertAsync(provider);
        var loaded = await store.GetAsync("primary");

        Assert.NotNull(loaded);
        Assert.Equal(provider.Id, loaded.Id);
        Assert.Equal(provider.ApiKey, loaded.ApiKey);
        Assert.Equal(provider.Priority, loaded.Priority);
        Assert.Equal(provider.Timeout, loaded.Timeout);
        Assert.Equal(provider.Models, loaded.Models);
        Assert.Equal(provider.DefaultModel, loaded.DefaultModel);
        Assert.False(loaded.DiscoverModels);
        Assert.Equal("yes", loaded.ExtraHeaders!["X-Test"]);
        Assert.False(loaded.SupportsNativeResponses);
    }

    [Fact]
    public async Task Provider_update_and_delete_are_persistent()
    {
        var store = ProviderStore();
        await store.UpsertAsync(Provider("primary", "secret"));
        await store.UpsertAsync(Provider("primary", "new-secret") with { Name = "Updated", Enabled = false });

        var reopened = ProviderStore();
        var updated = await reopened.GetAsync("primary");
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("new-secret", updated.ApiKey);
        Assert.False(updated.Enabled);

        await reopened.DeleteAsync("primary");
        Assert.Null(await ProviderStore().GetAsync("primary"));
    }

    [Fact]
    public async Task Provider_list_survives_store_recreation()
    {
        await ProviderStore().UpsertAsync(Provider("a", "one"));
        await ProviderStore().UpsertAsync(Provider("b", "two"));

        var providers = await ProviderStore().ListAsync();

        Assert.Equal(["a", "b"], providers.Select(x => x.Id).Order());
    }

    [Fact]
    public async Task Route_round_trip_preserves_order_and_targets()
    {
        var store = RouteStore();
        var route = new RouteDefinition(
            "coding",
            RoutingStrategy.RoundRobin,
            [
                new RouteTarget("primary", "model-a", 10, true),
                new RouteTarget("backup", "model-b", 20, false)
            ],
            Enabled: true);

        await store.UpsertAsync(route);
        var loaded = await RouteStore().GetAsync("coding");

        Assert.NotNull(loaded);
        Assert.Equal(RoutingStrategy.RoundRobin, loaded.Strategy);
        Assert.Equal(2, loaded.Targets.Count);
        Assert.Equal("primary", loaded.Targets[0].ProviderId);
        Assert.Equal("backup", loaded.Targets[1].ProviderId);
        Assert.False(loaded.Targets[1].Enabled);
    }

    [Fact]
    public async Task Route_update_replaces_targets_and_delete_is_persistent()
    {
        var store = RouteStore();
        await store.UpsertAsync(new RouteDefinition("coding", RoutingStrategy.Fallback, [new RouteTarget("a", "m1")]));
        await store.UpsertAsync(new RouteDefinition("coding", RoutingStrategy.RoundRobin, [new RouteTarget("b", "m2", 5)]));

        var updated = await RouteStore().GetAsync("coding");
        Assert.Equal(RoutingStrategy.RoundRobin, updated!.Strategy);
        Assert.Single(updated.Targets);
        Assert.Equal("b", updated.Targets[0].ProviderId);

        await RouteStore().DeleteAsync("coding");
        Assert.Null(await RouteStore().GetAsync("coding"));
    }

    [Fact]
    public async Task Empty_database_lists_are_empty()
    {
        Assert.Empty(await ProviderStore().ListAsync());
        Assert.Empty(await RouteStore().ListAsync());
    }

    private SqliteProviderStore ProviderStore() => new(new SqliteStoreOptions($"Data Source={_path}"));
    private SqliteRouteStore RouteStore() => new(new SqliteStoreOptions($"Data Source={_path}"));

    private static ProviderDefinition Provider(string id, string apiKey) =>
        new(id, id, "openai-compatible", "https://example.test/v1", apiKey, Models: ["model"], DefaultModel: "model");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + "-shm")) File.Delete(_path + "-shm");
        if (File.Exists(_path + "-wal")) File.Delete(_path + "-wal");
    }
}
