using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.Tests;

public sealed class EmbeddedUsageTests
{
    [Fact]
    public async Task Core_can_be_used_directly_without_web_application_or_sqlite()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAiProviderFactory, FakeProviderFactory>();
        services.AddAiRouter();

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IProviderManager>();
        var routes = provider.GetRequiredService<IRouteStore>();
        var router = provider.GetRequiredService<IAiRouter>();

        await manager.AddAsync(new ProviderDefinition(
            "fake",
            "Fake",
            "fake",
            "https://unused.test",
            null,
            Models: ["model-a"],
            DefaultModel: "model-a"));
        await routes.UpsertAsync(new RouteDefinition(
            "coding",
            RoutingStrategy.Fallback,
            [new RouteTarget("fake", "model-a")]));

        var body = JsonSerializer.SerializeToElement(new
        {
            model = "coding",
            messages = new[] { new { role = "user", content = "hello" } }
        });

        var result = await router.ChatAsync("coding", body);

        Assert.True(result.Success);
        Assert.Equal("fake", result.ProviderId);
        Assert.Equal("model-a", result.Model);
        Assert.Equal("ok", result.Body!.Value.GetProperty("result").GetString());
    }

    private sealed class FakeProviderFactory : IAiProviderFactory
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
                Body = JsonSerializer.SerializeToElement(new { result = "ok" })
            });

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            SendChatAsync(model, requestBody, stream, ct);

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? []);

        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectivityResult(true));
    }
}
