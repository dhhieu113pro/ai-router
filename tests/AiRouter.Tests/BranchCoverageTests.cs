using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.Tests;

public sealed class BranchCoverageTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public void AddAiRouter_supports_omitted_and_explicit_configuration()
    {
        var defaults = new ServiceCollection();
        defaults.AddAiRouter();
        Assert.Contains(defaults, descriptor => descriptor.ServiceType == typeof(AiRouterOptions));

        var configured = new ServiceCollection();
        configured.AddAiRouter(options => options.ConsecutiveFailuresBeforeCooldown = 7);
        using var provider = configured.BuildServiceProvider();
        Assert.Equal(7, provider.GetRequiredService<AiRouterOptions>().ConsecutiveFailuresBeforeCooldown);
    }

    [Fact]
    public async Task Provider_update_can_replace_secret_and_disabled_provider_can_resolve_models_from_store()
    {
        var store = new InMemoryProviderStore();
        var manager = new ProviderManager(store, [new Factory()]);
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary") with { ApiKey = "old" });

        var updated = await manager.UpdateAsync("primary", Definition("primary") with { ApiKey = "new" });
        Assert.Equal("new", updated.ApiKey);
        Assert.Equal("new", (await manager.GetAsync("primary"))!.ApiKey);

        await manager.SetEnabledAsync("primary", false);
        Assert.Empty(manager.Snapshot);
        Assert.Equal(["model"], await manager.ListModelsAsync("primary"));
    }

    [Fact]
    public async Task Resolver_handles_null_model_and_null_configured_model_collection()
    {
        var store = new InMemoryProviderStore();
        var manager = new ProviderManager(store, [new Factory()]);
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary") with { Models = null, DefaultModel = "default", DiscoverModels = false });
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync(null!));
        var all = await resolver.ResolveAsync("all");
        Assert.Equal("default", all.Targets.Single().Model);
    }

    [Fact]
    public async Task Rate_limit_with_expired_retry_after_uses_configured_cooldown()
    {
        var fixture = await CreateRouterAsync(new AiRouterOptions { RateLimitCooldown = TimeSpan.FromMinutes(3) });
        fixture.Provider.Response = new ProviderResponse
        {
            Success = false,
            StatusCode = 429,
            FailureKind = ProviderFailureKind.RateLimited,
            RetryAfter = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var before = DateTimeOffset.UtcNow.AddMinutes(2);

        await fixture.Router.ChatAsync("primary/model", Body);

        Assert.True(fixture.Provider.Health.CooldownUntil > before);
    }

    [Fact]
    public async Task Zero_failure_threshold_is_normalized_to_one()
    {
        var fixture = await CreateRouterAsync(new AiRouterOptions
        {
            ConsecutiveFailuresBeforeCooldown = 0,
            ErrorCooldown = TimeSpan.FromMinutes(1)
        });
        fixture.Provider.Response = ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down");

        await fixture.Router.ChatAsync("primary/model", Body);

        Assert.Equal(ProviderStatus.CoolingDown, fixture.Provider.Health.Status);
    }

    private static async Task<Fixture> CreateRouterAsync(AiRouterOptions options)
    {
        var factory = new Factory();
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary"));
        var router = new AiRouterService(new RouteResolver(manager, new InMemoryRouteStore()), manager, options);
        return new Fixture(router, factory.Last!);
    }

    private static ProviderDefinition Definition(string id) =>
        new(id, id, "fake", "https://example.test", "secret", Models: ["model"], DefaultModel: "model");

    private sealed record Fixture(AiRouterService Router, Provider Provider);

    private sealed class Factory : IAiProviderFactory
    {
        public Provider? Last { get; private set; }
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => Last = new Provider(definition);
    }

    private sealed class Provider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public ProviderResponse Response { get; set; } = new() { Success = true, StatusCode = 200 };
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(Response);
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(Response);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? ["model"]);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }
}
