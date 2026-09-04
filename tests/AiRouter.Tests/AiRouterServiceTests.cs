using System.Collections.Concurrent;
using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class AiRouterServiceTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Fallback_uses_priority_order_and_stops_on_success()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")),
            ("second", Success()));

        var result = await fixture.Router.ChatAsync("route", Body);

        Assert.True(result.Success);
        Assert.Equal("second", result.ProviderId);
        Assert.Equal(["first", "second"], fixture.Calls.ToArray());
    }

    [Fact]
    public async Task Invalid_request_is_terminal_and_does_not_fallback()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.InvalidRequest, 400, "bad")),
            ("second", Success()));

        var result = await fixture.Router.ChatAsync("route", Body);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(["first"], fixture.Calls.ToArray());
    }

    [Fact]
    public async Task Pinned_request_never_crosses_provider()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")),
            ("second", Success()));

        var result = await fixture.Router.ChatAsync("first/model", Body);

        Assert.False(result.Success);
        Assert.Equal(["first"], fixture.Calls.ToArray());
    }

    [Fact]
    public async Task Round_robin_rotates_starting_target()
    {
        var fixture = await CreateAsync(RoutingStrategy.RoundRobin,
            ("first", Success()),
            ("second", Success()));

        var one = await fixture.Router.ChatAsync("route", Body);
        var two = await fixture.Router.ChatAsync("route", Body);

        Assert.Equal("first", one.ProviderId);
        Assert.Equal("second", two.ProviderId);
    }

    [Fact]
    public async Task Round_robin_falls_through_remaining_targets()
    {
        var fixture = await CreateAsync(RoutingStrategy.RoundRobin,
            ("first", ProviderResponse.Failed(ProviderFailureKind.TargetFailure, 404, "model missing")),
            ("second", Success()));

        var result = await fixture.Router.ChatAsync("route", Body);

        Assert.True(result.Success);
        Assert.Equal("second", result.ProviderId);
    }

    [Fact]
    public async Task Provider_failure_enters_cooldown_and_is_skipped_while_alternative_exists()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")),
            ("second", Success()));

        await fixture.Router.ChatAsync("route", Body);
        fixture.Calls.Clear();
        await fixture.Router.ChatAsync("route", Body);

        Assert.Equal(["second"], fixture.Calls.ToArray());
    }

    [Fact]
    public async Task All_cooled_down_targets_get_one_last_resort_pass()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")),
            ("second", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")));

        await fixture.Router.ChatAsync("route", Body);
        fixture.Calls.Clear();
        await fixture.Router.ChatAsync("route", Body);

        Assert.Equal(2, fixture.Calls.Count);
    }

    [Fact]
    public async Task Success_resets_provider_failure_health()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback,
            ("first", ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, "down")),
            ("second", Success()));

        await fixture.Router.ChatAsync("route", Body);
        fixture.Provider("first").SetResponses(Success());
        await fixture.Router.ChatAsync("first/model", Body);

        Assert.Equal(0, fixture.Provider("first").Health.ConsecutiveFailures);
        Assert.Equal(ProviderStatus.Healthy, fixture.Provider("first").Health.Status);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var fixture = await CreateAsync(RoutingStrategy.Fallback, ("first", Success()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Router.ChatAsync("route", Body, ct: cts.Token));
    }

    [Fact]
    public async Task Committed_stream_failure_never_falls_back()
    {
        var committed = new ProviderResponse
        {
            Success = false,
            StatusCode = 502,
            FailureKind = ProviderFailureKind.ProviderFailure,
            ErrorMessage = "stream broke",
            StreamCommitted = true
        };
        var fixture = await CreateAsync(RoutingStrategy.Fallback, ("first", committed), ("second", Success()));

        var result = await fixture.Router.ChatAsync("route", Body, stream: true);

        Assert.False(result.Success);
        Assert.Equal(["first"], fixture.Calls.ToArray());
    }

    [Fact]
    public async Task Concurrent_round_robin_requests_use_both_targets()
    {
        var fixture = await CreateAsync(RoutingStrategy.RoundRobin, ("first", Success()), ("second", Success()));
        var tasks = Enumerable.Range(0, 20).Select(_ => fixture.Router.ChatAsync("route", Body));
        var results = await Task.WhenAll(tasks);

        Assert.Contains(results, result => result.ProviderId == "first");
        Assert.Contains(results, result => result.ProviderId == "second");
    }

    private static ProviderResponse Success() => new() { Success = true, StatusCode = 200, Body = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone() };

    private static async Task<Fixture> CreateAsync(RoutingStrategy strategy, params (string Id, ProviderResponse Response)[] providers)
    {
        var calls = new ConcurrentQueue<string>();
        var factory = new FakeFactory(calls);
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        var routeStore = new InMemoryRouteStore();

        var targets = new List<RouteTarget>();
        var priority = 0;
        foreach (var item in providers)
        {
            await manager.AddAsync(new ProviderDefinition(item.Id, item.Id, "fake", "https://example.test", "key", Priority: priority, Models: ["model"], DefaultModel: "model"));
            factory.Get(item.Id).SetResponses(item.Response);
            targets.Add(new RouteTarget(item.Id, "model", priority));
            priority += 10;
        }

        await routeStore.UpsertAsync(new RouteDefinition("route", strategy, targets));
        var resolver = new RouteResolver(manager, routeStore);
        return new Fixture(new AiRouterService(resolver, manager, new AiRouterOptions()), calls, factory);
    }

    private sealed record Fixture(AiRouterService Router, ConcurrentQueue<string> Calls, FakeFactory Factory)
    {
        public FakeProvider Provider(string id) => Factory.Get(id);
    }

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
            lock (_responses)
            {
                var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
                return Task.FromResult(response);
            }
        }

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => SendChatAsync(model, requestBody, stream, ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? []);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }
}
