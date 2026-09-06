using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class CacheAffinityCoverageRegressionTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Sticky_route_marks_stale_affinity_as_miss_and_selects_an_eligible_target()
    {
        var fixture = await CreateAsync();
        const string key = "stale";
        fixture.Affinity.Set("route", key, new ResolvedTarget("removed", "model"), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        var result = await fixture.Router.ChatAsync("route", Body, new RouterRequestContext(key, "header"));

        Assert.True(result.Success);
        Assert.Equal("miss", result.AffinityClassification);
        Assert.Contains(result.ProviderId, new[] { "first", "second" });
    }

    [Fact]
    public async Task Telemetry_failure_never_changes_successful_routing_result()
    {
        var fixture = await CreateAsync(new ThrowingTelemetry());

        var result = await fixture.Router.ChatAsync("first/model", Body);

        Assert.True(result.Success);
        Assert.Equal("first", result.ProviderId);
    }

    [Fact]
    public void Summary_orders_equal_count_provider_and_route_groups_by_key()
    {
        var telemetry = new InMemoryRouterTelemetry();
        telemetry.Record(Record("route-b", "provider-b"));
        telemetry.Record(Record("route-a", "provider-a"));

        var summary = telemetry.Summary();

        Assert.Equal(new[] { "provider-a", "provider-b" }, summary.Providers.Select(x => x.Key));
        Assert.Equal(new[] { "route-a", "route-b" }, summary.Routes.Select(x => x.Key));
    }

    private static RouterTelemetryRecord Record(string route, string provider) => new(
        DateTimeOffset.UtcNow,
        route,
        provider,
        "model",
        RoutingStrategy.Sticky,
        false,
        true,
        false,
        "hit",
        1,
        TimeSpan.FromMilliseconds(1),
        new ProviderUsage(10, 2, 12, 5, null, null),
        null,
        null,
        true,
        200,
        ProviderFailureKind.None);

    private static async Task<Fixture> CreateAsync(IRouterTelemetry? telemetry = null)
    {
        var factory = new FakeFactory();
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        await manager.AddAsync(new ProviderDefinition("first", "first", "fake", "https://example.test", "key", Priority: 0, Models: ["model"], DefaultModel: "model"));
        await manager.AddAsync(new ProviderDefinition("second", "second", "fake", "https://example.test", "key", Priority: 10, Models: ["model"], DefaultModel: "model"));
        var routes = new InMemoryRouteStore();
        await routes.UpsertAsync(new RouteDefinition("route", RoutingStrategy.Sticky,
        [
            new RouteTarget("first", "model", 0),
            new RouteTarget("second", "model", 10)
        ]));
        var affinity = new InMemoryAffinityStore();
        var router = new AiRouterService(new RouteResolver(manager, routes), manager, new AiRouterOptions(), affinity, telemetry);
        return new Fixture(router, affinity);
    }

    private sealed record Fixture(AiRouterService Router, InMemoryAffinityStore Affinity);

    private sealed class ThrowingTelemetry : IRouterTelemetry
    {
        public void Record(RouterTelemetryRecord record) => throw new InvalidOperationException("telemetry unavailable");
        public IReadOnlyList<RouterTelemetryRecord> Recent() => [];
        public RouterTelemetrySummary Summary() => throw new NotSupportedException();
    }

    private sealed class FakeFactory : IAiProviderFactory
    {
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => new FakeProvider(definition);
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            Task.FromResult(new ProviderResponse
            {
                Success = true,
                StatusCode = 200,
                Body = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone()
            });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => SendChatAsync(model, requestBody, stream, ct);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["model"]);
    }
}
