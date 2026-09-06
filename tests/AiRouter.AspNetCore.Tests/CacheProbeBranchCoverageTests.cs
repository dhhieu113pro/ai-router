using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.AspNetCore.Tests;

public sealed class CacheProbeBranchCoverageTests
{
    [Fact]
    public async Task Probe_uses_responses_path_for_input_requests()
    {
        var router = new RecordingRouter(Result(null, null));
        var providers = await EmptyProvidersAsync();
        var request = new CacheProbeRequest("coding", JsonDocument.Parse("{\"input\":[]}").RootElement.Clone(), 1);

        var result = await CacheProbe.RunAsync(router, providers, request);

        Assert.Equal(1, router.ResponsesCalls);
        Assert.Equal(0, router.ChatCalls);
        Assert.Contains("cache_data_unavailable", result.Diagnostics);
        Assert.Null(result.CacheRatio);
        Assert.Null(result.Recommendation);
    }

    [Fact]
    public async Task Probe_uses_chat_path_when_messages_exist_even_with_input()
    {
        var router = new RecordingRouter(Result("missing-provider", new ProviderUsage(0, 0, 0, 0, null, null)));
        var providers = await EmptyProvidersAsync();
        var request = new CacheProbeRequest("coding", JsonDocument.Parse("{\"messages\":[],\"input\":[]}").RootElement.Clone(), 1);

        var result = await CacheProbe.RunAsync(router, providers, request);

        Assert.Equal(1, router.ChatCalls);
        Assert.Equal(0, router.ResponsesCalls);
        Assert.Null(result.CacheRatio);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Probe_reports_zero_cache_ratio_when_usage_is_known_and_uncached()
    {
        var router = new RecordingRouter(Result("p1", new ProviderUsage(10, 1, 11, 0, null, null)));
        var providers = await EmptyProvidersAsync();
        var request = new CacheProbeRequest("coding", JsonDocument.Parse("{}").RootElement.Clone(), 1);

        var result = await CacheProbe.RunAsync(router, providers, request);

        Assert.Equal(0m, result.CacheRatio);
        Assert.Contains("cache_ratio_zero", result.Diagnostics);
    }

    [Fact]
    public async Task Probe_resolves_provider_pricing_when_target_is_known()
    {
        var providers = await PricedProvidersAsync();
        var router = new RecordingRouter(Result("priced", new ProviderUsage(10, 2, 12, 5, null, null)));
        var request = new CacheProbeRequest("coding", JsonDocument.Parse("{}").RootElement.Clone(), 1);

        var result = await CacheProbe.RunAsync(router, providers, request);

        Assert.NotNull(result.Attempts[0].Cost);
        Assert.Equal("estimated", result.Attempts[0].CostSource);
    }

    [Fact]
    public async Task Probe_null_arguments_are_rejected()
    {
        var providers = await EmptyProvidersAsync();
        var request = new CacheProbeRequest("coding", JsonDocument.Parse("{}").RootElement.Clone(), 1);
        var router = new RecordingRouter(Result(null, null));

        await Assert.ThrowsAsync<ArgumentNullException>(() => CacheProbe.RunAsync(null!, providers, request));
        await Assert.ThrowsAsync<ArgumentNullException>(() => CacheProbe.RunAsync(router, null!, request));
        await Assert.ThrowsAsync<ArgumentNullException>(() => CacheProbe.RunAsync(router, providers, null!));
    }

    private static RouterResult Result(string? provider, ProviderUsage? usage)
    {
        JsonElement? body = usage is null
            ? null
            : JsonSerializer.SerializeToElement(new
            {
                usage = new
                {
                    prompt_tokens = usage.InputTokens,
                    completion_tokens = usage.OutputTokens,
                    total_tokens = usage.TotalTokens,
                    prompt_tokens_details = new { cached_tokens = usage.CachedInputTokens }
                }
            });

        return new RouterResult
        {
            Success = true,
            StatusCode = 200,
            ProviderId = provider,
            Model = "model",
            Body = body,
            AffinityClassification = "hit",
            AttemptCount = 1
        };
    }

    private static async Task<IProviderManager> EmptyProvidersAsync()
    {
        var manager = new ProviderManager(new InMemoryProviderStore(), Array.Empty<IAiProviderFactory>());
        await manager.InitializeAsync();
        return manager;
    }

    private static async Task<IProviderManager> PricedProvidersAsync()
    {
        var factory = new FakeFactory();
        var manager = new ProviderManager(new InMemoryProviderStore(), [factory]);
        await manager.InitializeAsync();
        await manager.AddAsync(new ProviderDefinition(
            "priced", "priced", "fake", "https://example.test", "key",
            Models: ["model"], DefaultModel: "model",
            InputPricePerMillion: 1m, CachedInputPricePerMillion: 0.1m, OutputPricePerMillion: 2m));
        return manager;
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
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => SendChatAsync(model, requestBody, stream, ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? []);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }

    private sealed class RecordingRouter(RouterResult result) : IAiRouter
    {
        public int ChatCalls { get; private set; }
        public int ResponsesCalls { get; private set; }

        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default)
        {
            ChatCalls++;
            return Task.FromResult(result);
        }

        public Task<RouterResult> ChatAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) => ChatAsync(model, body, stream, ct);

        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default)
        {
            ResponsesCalls++;
            return Task.FromResult(result);
        }

        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) => ResponsesAsync(model, body, stream, ct);
    }
}
