using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class AiRouterFailureKindCoverageTests
{
    private static readonly JsonElement Body = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Unknown_failure_kind_uses_default_health_branch()
    {
        var factory = new Factory();
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        await manager.AddAsync(new ProviderDefinition(
            "primary",
            "Primary",
            "fake",
            "https://example.test",
            "secret",
            Models: ["model"],
            DefaultModel: "model"));

        var router = new AiRouterService(
            new RouteResolver(manager, new InMemoryRouteStore()),
            manager,
            new AiRouterOptions());

        factory.Last!.Response = ProviderResponse.Failed((ProviderFailureKind)999, 500, "unknown");

        var result = await router.ChatAsync("primary/model", Body);

        Assert.False(result.Success);
        Assert.Equal((ProviderFailureKind)999, result.FailureKind);
        Assert.Equal(ProviderStatus.Healthy, factory.Last.Health.Status);
        Assert.Equal(0, factory.Last.Health.ConsecutiveFailures);
        Assert.NotNull(factory.Last.Health.LastFailureAt);
    }

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

        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            Task.FromResult(Response);

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            Task.FromResult(Response);

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? ["model"]);

        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectivityResult(true));
    }
}
