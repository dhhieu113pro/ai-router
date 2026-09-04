using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class RouteResolverTests
{
    [Fact]
    public async Task Provider_model_is_pinned()
    {
        var (resolver, _, _) = await CreateAsync();
        var route = await resolver.ResolveAsync("primary/model-b");
        Assert.True(route.Pinned);
        Assert.Equal([new ResolvedTarget("primary", "model-b")], route.Targets);
    }

    [Fact]
    public async Task Provider_id_uses_default_model()
    {
        var (resolver, _, _) = await CreateAsync();
        var route = await resolver.ResolveAsync("primary");
        Assert.True(route.Pinned);
        Assert.Equal("model-a", route.Targets.Single().Model);
    }

    [Fact]
    public async Task Provider_without_default_model_is_rejected()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("nodefault", 10) with { DefaultModel = null });
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());
        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync("nodefault"));
    }

    [Fact]
    public async Task Logical_route_orders_target_then_provider_priority()
    {
        var (resolver, _, routes) = await CreateAsync();
        await routes.UpsertAsync(new RouteDefinition("coding", RoutingStrategy.Fallback,
        [
            new RouteTarget("secondary", "model-b", 20),
            new RouteTarget("primary", "model-a", 10)
        ]));

        var route = await resolver.ResolveAsync("coding");
        Assert.False(route.Pinned);
        Assert.Equal([new ResolvedTarget("primary", "model-a"), new ResolvedTarget("secondary", "model-b")], route.Targets);
    }

    [Fact]
    public async Task All_expands_enabled_provider_models()
    {
        var (resolver, _, _) = await CreateAsync();
        var route = await resolver.ResolveAsync("all");
        Assert.Equal(RoutingStrategy.Fallback, route.Strategy);
        Assert.Equal(4, route.Targets.Count);
        Assert.Equal("primary", route.Targets[0].ProviderId);
    }

    [Fact]
    public async Task Unknown_model_is_rejected()
    {
        var (resolver, _, _) = await CreateAsync();
        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync("missing/model"));
    }

    private static async Task<(RouteResolver Resolver, ProviderManager Providers, InMemoryRouteStore Routes)> CreateAsync()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", 10));
        await manager.AddAsync(Definition("secondary", 20));
        var routes = new InMemoryRouteStore();
        return (new RouteResolver(manager, routes), manager, routes);
    }

    private static ProviderManager CreateManager() => new(new InMemoryProviderStore(), [new FakeFactory()]);

    private static ProviderDefinition Definition(string id, int priority) =>
        new(id, id, "fake", "https://example.test", "key", Priority: priority, Models: ["model-a", "model-b"], DefaultModel: "model-a");

    private sealed class FakeFactory : IAiProviderFactory
    {
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => new FakeProvider(definition);
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult(Definition.Models ?? []);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }
}
