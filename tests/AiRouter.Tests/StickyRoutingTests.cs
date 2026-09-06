using System.Collections.Concurrent;
using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class StickyRoutingTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Same_affinity_key_reuses_same_target()
    {
        var fixture = await CreateAsync(Success(), Success());
        var ctx = new RouterRequestContext("session-a", "header");

        var first = await fixture.Router.ChatAsync("route", Body, ctx);
        fixture.Calls.Clear();
        var second = await fixture.Router.ChatAsync("route", Body, ctx);

        Assert.Equal(first.ProviderId, second.ProviderId);
        Assert.Equal("hit", second.AffinityClassification);
        Assert.True(second.AffinityApplied);
        Assert.Single(fixture.Calls);
    }

    [Fact]
    public async Task Sticky_route_rebinds_after_rate_limit()
    {
        var fixture = await CreateAsync(
            ProviderResponse.Failed(ProviderFailureKind.RateLimited, 429, "slow down"),
            Success());
        var ctx = new RouterRequestContext("session-hash", "header");

        var result = await fixture.Router.ChatAsync("route", Body, ctx);

        Assert.True(result.Success);
        Assert.True(result.FallbackOccurred);
        Assert.True(result.AffinityRebound);
        Assert.Equal(2, result.AttemptCount);
    }

    [Fact]
    public async Task Invalid_request_does_not_retry()
    {
        var fixture = await CreateAsync(
            ProviderResponse.Failed(ProviderFailureKind.InvalidRequest, 400, "bad"),
            Success());

        var result = await fixture.Router.ChatAsync("route", Body, new RouterRequestContext("fixed", "header"));

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Single(fixture.Calls);
    }

    [Fact]
    public async Task Direct_provider_model_is_pinned()
    {
        var fixture = await CreateAsync(Success(), Success());
        var result = await fixture.Router.ChatAsync("first/model", Body, new RouterRequestContext("fixed", "header"));
        Assert.Equal("first", result.ProviderId);
        Assert.Equal("pinned", result.AffinityClassification);
    }

    private static ProviderResponse Success() => new()
    {
        Success = true,
        StatusCode = 200,
        Body = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone()
    };

    private static async Task<Fixture> CreateAsync(ProviderResponse firstResponse, ProviderResponse secondResponse)
    {
        var calls = new ConcurrentQueue<string>();
        var factory = new FakeFactory(calls);
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        var routeStore = new InMemoryRouteStore();

        await manager.AddAsync(new ProviderDefinition("first", "first", "fake", "https://example.test", "key", Priority: 0, Models: ["model"], DefaultModel: "model"));
        await manager.AddAsync(new ProviderDefinition("second", "second", "fake", "https://example.test", "key", Priority: 10, Models: ["model"], DefaultModel: "model"));
        factory.Get("first").SetResponses(firstResponse, Success());
        factory.Get("second").SetResponses(secondResponse, Success());
        await routeStore.UpsertAsync(new RouteDefinition("route", RoutingStrategy.Sticky,
        [
            new RouteTarget("first", "model", 0),
            new RouteTarget("second", "model", 10)
        ]));

        var router = new AiRouterService(new RouteResolver(manager, routeStore), manager, new AiRouterOptions(), new InMemoryAffinityStore());
        return new Fixture(router, calls);
    }

    private sealed record Fixture(AiRouterService Router, ConcurrentQueue<string> Calls);

    private sealed class FakeFactory(ConcurrentQueue<string> calls) : IAiProviderFactory
    {
        private readonly ConcurrentDictionary<string, FakeProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => _providers.GetOrAdd(definition.Id, _ => new FakeProvider(definition, calls));
        public FakeProvider Get(string id) => _providers[id];
    }

    private sealed class FakeProvider(ProviderDefinition definition, ConcurrentQueue<string> calls) : IAiProvider
    {
        private readonly Queue<ProviderResponse> _responses = new();
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();

        public void SetResponses(params ProviderResponse[] responses)
        {
            lock (_responses)
            {
                _responses.Clear();
                foreach (var response in responses) _responses.Enqueue(response);
            }
        }

        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            calls.Enqueue(Definition.Id);
            lock (_responses) return Task.FromResult(_responses.Count > 1 ? _responses.Dequeue() : _responses.Peek());
        }

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            SendChatAsync(model, requestBody, stream, ct);

        public Task<ProviderConnectivityResult> TestConnectivityAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["model"]);
    }
}
