using System.Collections.Concurrent;
using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Models;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class CoreCoverageEdgeTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Router_maps_route_resolution_errors_to_invalid_request()
    {
        var manager = Manager();
        await manager.InitializeAsync();
        var router = new AiRouterService(new RouteResolver(manager, new InMemoryRouteStore()), manager);

        var result = await router.ChatAsync("missing", Body);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(ProviderFailureKind.InvalidRequest, result.FailureKind);
        Assert.Contains("Unknown model or route", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Router_returns_unavailable_when_provider_disappears_after_resolution()
    {
        var provider = new TestProvider(Definition("primary"));
        var manager = new SequenceProviderManager(provider);
        var router = new AiRouterService(new RouteResolver(manager, new InMemoryRouteStore()), manager);

        var result = await router.ChatAsync("primary/model", Body);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("No enabled providers are available for this route.", result.ErrorMessage);
    }

    [Fact]
    public async Task Router_responses_dispatches_to_responses_provider_method()
    {
        var fixture = await RouterFixture.CreateAsync();
        fixture.Provider.ResponsesHandler = _ => Task.FromResult(Success(contentType: "application/json"));

        var result = await fixture.Router.ResponsesAsync("primary/model", Body);

        Assert.True(result.Success);
        Assert.Equal(1, fixture.Provider.ResponsesCalls);
        Assert.Equal(0, fixture.Provider.ChatCalls);
    }

    [Fact]
    public async Task Router_converts_provider_timeout_cancellation_to_504()
    {
        var fixture = await RouterFixture.CreateAsync();
        fixture.Provider.ChatHandler = _ => Task.FromException<ProviderResponse>(new OperationCanceledException("upstream timeout"));

        var result = await fixture.Router.ChatAsync("primary/model", Body);

        Assert.False(result.Success);
        Assert.Equal(504, result.StatusCode);
        Assert.Equal(ProviderFailureKind.ProviderFailure, result.FailureKind);
    }

    [Fact]
    public async Task Router_converts_unexpected_provider_exception_to_503()
    {
        var fixture = await RouterFixture.CreateAsync();
        fixture.Provider.ChatHandler = _ => Task.FromException<ProviderResponse>(new InvalidOperationException("boom"));

        var result = await fixture.Router.ChatAsync("primary/model", Body);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("boom", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Router_propagates_cancellation_that_occurs_inside_provider_send()
    {
        var fixture = await RouterFixture.CreateAsync();
        using var cts = new CancellationTokenSource();
        fixture.Provider.ChatHandler = token =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Success());
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Router.ChatAsync("primary/model", Body, ct: cts.Token));
    }

    [Fact]
    public async Task Expired_cooldown_is_cleared_before_request()
    {
        var fixture = await RouterFixture.CreateAsync();
        fixture.Provider.Health.Status = ProviderStatus.CoolingDown;
        fixture.Provider.Health.CooldownUntil = DateTimeOffset.UtcNow.AddMinutes(-1);

        var result = await fixture.Router.ChatAsync("primary/model", Body);

        Assert.True(result.Success);
        Assert.Null(fixture.Provider.Health.CooldownUntil);
        Assert.Equal(ProviderStatus.Healthy, fixture.Provider.Health.Status);
    }

    [Fact]
    public async Task Rate_limit_sets_retry_after_cooldown()
    {
        var fixture = await RouterFixture.CreateAsync();
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(2);
        fixture.Provider.ChatHandler = _ => Task.FromResult(new ProviderResponse
        {
            Success = false,
            StatusCode = 429,
            FailureKind = ProviderFailureKind.RateLimited,
            ErrorMessage = "slow down",
            RetryAfter = retryAfter
        });

        var result = await fixture.Router.ChatAsync("primary/model", Body);

        Assert.False(result.Success);
        Assert.Equal(ProviderStatus.CoolingDown, fixture.Provider.Health.Status);
        Assert.Equal(1, fixture.Provider.Health.ConsecutiveFailures);
        Assert.Equal(retryAfter, fixture.Provider.Health.CooldownUntil);
    }

    [Fact]
    public async Task First_provider_failure_can_degrade_before_threshold()
    {
        var fixture = await RouterFixture.CreateAsync(new AiRouterOptions
        {
            ConsecutiveFailuresBeforeCooldown = 2,
            ErrorCooldown = TimeSpan.FromMinutes(1)
        });
        fixture.Provider.ChatHandler = _ => Task.FromResult(ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down"));

        await fixture.Router.ChatAsync("primary/model", Body);

        Assert.Equal(ProviderStatus.Degraded, fixture.Provider.Health.Status);
        Assert.Equal(1, fixture.Provider.Health.ConsecutiveFailures);
        Assert.Null(fixture.Provider.Health.CooldownUntil);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolver_rejects_empty_model(string model)
    {
        var manager = Manager();
        await manager.InitializeAsync();
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync(model));
    }

    [Theory]
    [InlineData("/model")]
    [InlineData("primary/")]
    public async Task Resolver_rejects_malformed_direct_model(string model)
    {
        var manager = Manager();
        await manager.InitializeAsync();
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync(model));
    }

    [Fact]
    public async Task Resolver_rejects_unknown_logical_route()
    {
        var manager = Manager();
        await manager.InitializeAsync();
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync("logical-missing"));
    }

    [Fact]
    public async Task Resolver_rejects_route_without_enabled_targets()
    {
        var manager = Manager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary"));
        var routes = new InMemoryRouteStore();
        await routes.UpsertAsync(new RouteDefinition("route", RoutingStrategy.Fallback,
            [new RouteTarget("primary", "model", Enabled: false)]));
        var resolver = new RouteResolver(manager, routes);

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync("route"));
    }

    [Fact]
    public async Task Resolver_all_discovers_models_when_configuration_is_empty()
    {
        var factory = new TestFactory { Models = ["discovered-b", "discovered-a"] };
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary") with { Models = [], DefaultModel = null, DiscoverModels = true });
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        var route = await resolver.ResolveAsync("all");

        Assert.Equal(["discovered-a", "discovered-b"], route.Targets.Select(x => x.Model).ToArray());
    }

    [Fact]
    public async Task Resolver_all_uses_default_model_when_no_models_are_configured()
    {
        var manager = Manager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary") with { Models = [], DefaultModel = "default-only", DiscoverModels = false });
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        var route = await resolver.ResolveAsync("all");

        Assert.Equal("default-only", route.Targets.Single().Model);
    }

    [Fact]
    public async Task Resolver_all_rejects_when_no_provider_models_exist()
    {
        var manager = Manager();
        await manager.InitializeAsync();
        var resolver = new RouteResolver(manager, new InMemoryRouteStore());

        await Assert.ThrowsAsync<RouteResolutionException>(() => resolver.ResolveAsync("all"));
    }

    [Fact]
    public async Task Provider_manager_update_rejects_body_id_mismatch()
    {
        var manager = Manager();
        await manager.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.UpdateAsync("route-id", Definition("body-id")));
    }

    [Fact]
    public async Task Provider_manager_can_resolve_disabled_provider_from_store_for_test()
    {
        var store = new InMemoryProviderStore();
        await store.UpsertAsync(Definition("disabled") with { Enabled = false });
        var manager = new ProviderManager(store, [new TestFactory()]);
        await manager.InitializeAsync();

        var result = await manager.TestAsync("disabled");

        Assert.True(result.Success);
        Assert.Empty(manager.Snapshot);
    }

    [Fact]
    public async Task Provider_manager_initialize_rejects_enabled_provider_without_factory()
    {
        var store = new InMemoryProviderStore();
        await store.UpsertAsync(Definition("unknown") with { Type = "unsupported" });
        var manager = new ProviderManager(store, [new TestFactory()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InitializeAsync());
    }

    [Fact]
    public async Task Provider_manager_rejects_missing_name()
    {
        var manager = Manager();
        await manager.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.AddAsync(Definition("primary") with { Name = " " }));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://example.test")]
    public async Task Provider_manager_rejects_non_http_base_url(string baseUrl)
    {
        var manager = Manager();
        await manager.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.AddAsync(Definition("primary") with { BaseUrl = baseUrl }));
    }

    [Fact]
    public async Task In_memory_route_store_lists_routes_in_id_order()
    {
        var store = new InMemoryRouteStore();
        await store.UpsertAsync(new RouteDefinition("z", RoutingStrategy.Fallback, []));
        await store.UpsertAsync(new RouteDefinition("a", RoutingStrategy.Fallback, []));

        var routes = await store.ListAsync();

        Assert.Equal(["a", "z"], routes.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void Chat_completion_response_accessors_round_trip_all_properties()
    {
        var choices = JsonDocument.Parse("[]").RootElement.Clone();
        var usage = JsonDocument.Parse("{\"total_tokens\":1}").RootElement.Clone();
        var extra = JsonDocument.Parse("true").RootElement.Clone();
        var response = new ChatCompletionResponse
        {
            Id = "chat-1",
            Object = "chat.completion",
            Created = 42,
            Model = "model",
            Choices = choices,
            Usage = usage,
            AdditionalProperties = new Dictionary<string, JsonElement> { ["extra"] = extra }
        };

        Assert.Equal("chat-1", response.Id);
        Assert.Equal("chat.completion", response.Object);
        Assert.Equal(42, response.Created);
        Assert.Equal("model", response.Model);
        Assert.Equal(JsonValueKind.Array, response.Choices!.Value.ValueKind);
        Assert.Equal(JsonValueKind.Object, response.Usage!.Value.ValueKind);
        Assert.True(response.AdditionalProperties["extra"].GetBoolean());
    }

    [Fact]
    public void Responses_response_accessors_round_trip_all_properties()
    {
        var output = JsonDocument.Parse("[]").RootElement.Clone();
        var usage = JsonDocument.Parse("{\"total_tokens\":1}").RootElement.Clone();
        var extra = JsonDocument.Parse("123").RootElement.Clone();
        var response = new ResponsesResponse
        {
            Id = "resp-1",
            Object = "response.custom",
            Model = "model",
            Output = output,
            Usage = usage,
            AdditionalProperties = new Dictionary<string, JsonElement> { ["extra"] = extra }
        };

        Assert.Equal("resp-1", response.Id);
        Assert.Equal("response.custom", response.Object);
        Assert.Equal("model", response.Model);
        Assert.Equal(JsonValueKind.Array, response.Output!.Value.ValueKind);
        Assert.Equal(JsonValueKind.Object, response.Usage!.Value.ValueKind);
        Assert.Equal(123, response.AdditionalProperties["extra"].GetInt32());
    }

    private static ProviderManager Manager() => new(new InMemoryProviderStore(), [new TestFactory()]);

    private static ProviderDefinition Definition(string id) =>
        new(id, id, "fake", "https://example.test", "secret", Models: ["model"], DefaultModel: "model");

    private static ProviderResponse Success(string? contentType = null) => new()
    {
        Success = true,
        StatusCode = 200,
        Body = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone(),
        ContentType = contentType
    };

    private sealed class TestFactory : IAiProviderFactory
    {
        private readonly ConcurrentDictionary<string, TestProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Models { get; init; } = ["model"];
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) =>
            _providers.GetOrAdd(definition.Id, _ => new TestProvider(definition) { Models = Models });
        public TestProvider Get(string id) => _providers[id];
    }

    private sealed class TestProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public IReadOnlyList<string> Models { get; init; } = definition.Models ?? [];
        public Func<CancellationToken, Task<ProviderResponse>> ChatHandler { get; set; } = _ => Task.FromResult(Success());
        public Func<CancellationToken, Task<ProviderResponse>> ResponsesHandler { get; set; } = _ => Task.FromResult(Success());
        public int ChatCalls { get; private set; }
        public int ResponsesCalls { get; private set; }

        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default)
        {
            ChatCalls++;
            return ChatHandler(ct);
        }

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default)
        {
            ResponsesCalls++;
            return ResponsesHandler(ct);
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult(Models);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectivityResult(true));
    }

    private sealed record RouterFixture(AiRouterService Router, TestProvider Provider)
    {
        public static async Task<RouterFixture> CreateAsync(AiRouterOptions? options = null)
        {
            var factory = new TestFactory();
            var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
            await manager.InitializeAsync();
            await manager.AddAsync(Definition("primary"));
            var router = new AiRouterService(new RouteResolver(manager, new InMemoryRouteStore()), manager, options);
            return new RouterFixture(router, factory.Get("primary"));
        }
    }

    private sealed class SequenceProviderManager(IAiProvider provider) : IProviderManager
    {
        private int _snapshotReads;
        public IReadOnlyList<IAiProvider> Snapshot => Interlocked.Increment(ref _snapshotReads) == 1 ? [provider] : [];
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProviderDefinition>>([provider.Definition]);
        public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ProviderDefinition?>(provider.Definition);
        public Task<ProviderDefinition> AddAsync(ProviderDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(provider.Definition.Models ?? []);
    }
}
