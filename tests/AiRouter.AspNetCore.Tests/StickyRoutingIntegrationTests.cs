using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class StickyRoutingIntegrationTests
{
    [Fact]
    public async Task Same_session_stays_on_target_then_rebinds_after_rate_limit()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-AiRouter-Session", "conversation-42");

        var first = await SendAsync(client);
        var second = await SendAsync(client);
        var third = await SendAsync(client);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("a", first.Headers.GetValues("X-AiRouter-Provider").Single());
        Assert.Equal("b", second.Headers.GetValues("X-AiRouter-Provider").Single());
        Assert.Equal("true", second.Headers.GetValues("X-AiRouter-Fallback").Single());
        Assert.Equal("b", third.Headers.GetValues("X-AiRouter-Provider").Single());
        Assert.Equal("hit", third.Headers.GetValues("X-AiRouter-Affinity").Single());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client) =>
        client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "coding",
            messages = new[] { new { role = "user", content = "ping" } }
        });

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var factory = new FakeFactory();
        builder.Services.AddSingleton<IAiProviderFactory>(factory);
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();

        var app = builder.Build();
        var manager = app.Services.GetRequiredService<IProviderManager>();
        await manager.InitializeAsync();
        await manager.AddAsync(new ProviderDefinition("a", "A", "fake-sticky", "https://unused.test", null, Models: ["model"], DefaultModel: "model"));
        await manager.AddAsync(new ProviderDefinition("b", "B", "fake-sticky", "https://unused.test", null, Priority: 10, Models: ["model"], DefaultModel: "model"));
        factory.Get("a").SetResponses(Success(), ProviderResponse.Failed(ProviderFailureKind.RateLimited, 429, "rate limited"));
        factory.Get("b").SetResponses(Success(), Success());

        var routeStore = app.Services.GetRequiredService<IRouteStore>();
        await routeStore.UpsertAsync(new RouteDefinition("coding", RoutingStrategy.Sticky,
        [
            new RouteTarget("a", "model", 0),
            new RouteTarget("b", "model", 10)
        ]));

        var affinityKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("conversation-42"))).ToLowerInvariant();
        app.Services.GetRequiredService<IAffinityStore>().Set("coding", affinityKey, new ResolvedTarget("a", "model"), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        app.MapAiRouterOpenAiEndpoints();
        await app.StartAsync();
        return app;
    }

    private static ProviderResponse Success() => new()
    {
        Success = true,
        StatusCode = 200,
        Body = JsonSerializer.SerializeToElement(new
        {
            id = "test",
            usage = new { prompt_tokens = 100, completion_tokens = 5, total_tokens = 105, prompt_tokens_details = new { cached_tokens = 80 } }
        })
    };

    private sealed class FakeFactory : IAiProviderFactory
    {
        private readonly ConcurrentDictionary<string, FakeProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake-sticky";
        public IAiProvider Create(ProviderDefinition definition) => _providers.GetOrAdd(definition.Id, _ => new FakeProvider(definition));
        public FakeProvider Get(string id) => _providers[id];
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
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
            lock (_responses) return Task.FromResult(_responses.Count > 1 ? _responses.Dequeue() : _responses.Peek());
        }

        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => SendChatAsync(model, requestBody, stream, ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["model"]);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }
}
