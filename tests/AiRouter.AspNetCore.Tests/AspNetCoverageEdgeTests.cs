using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class AspNetCoverageEdgeTests
{
    [Fact]
    public async Task Management_missing_resources_return_404_and_collections_return_200()
    {
        await using var app = await StartManagementAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/providers/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/providers/missing/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/providers/missing/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/providers/missing/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/providers/missing/models")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/providers/missing/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/routes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/routes/missing")).StatusCode);
    }

    [Fact]
    public async Task Management_rejects_malformed_null_duplicate_and_invalid_provider_bodies()
    {
        await using var app = await StartManagementAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/providers", JsonText("{"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/providers", JsonText("null"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/providers", ProviderBody("bad id", "https://unused.test"))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/providers", ProviderBody("primary", "https://unused.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/providers", ProviderBody("PRIMARY", "https://unused.test"))).StatusCode);
    }

    [Fact]
    public async Task Management_provider_update_covers_id_mismatch_missing_and_validation_errors()
    {
        await using var app = await StartManagementAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("primary", "https://unused.test"));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/providers/primary", ProviderBody("other", "https://unused.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/providers/missing", ProviderBody("missing", "https://unused.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/providers/primary", ProviderBody("primary", "ftp://invalid.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/providers/primary", JsonText("{"))).StatusCode);
    }

    [Fact]
    public async Task Management_enable_disable_test_models_and_disabled_health_round_trip()
    {
        await using var app = await StartManagementAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("primary", "https://unused.test"));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/providers/primary/disable", null)).StatusCode);
        var disabledHealth = await client.GetFromJsonAsync<JsonElement>("/providers/primary/health");
        Assert.Equal("Disabled", disabledHealth.GetProperty("status").GetString());
        Assert.Equal(0, disabledHealth.GetProperty("consecutiveFailures").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/providers/primary/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/providers/primary/test", null)).StatusCode);
        var models = await client.GetFromJsonAsync<string[]>("/providers/primary/models");
        Assert.Equal(["model-a"], models);
    }

    [Fact]
    public async Task Management_routes_cover_malformed_id_mismatch_and_missing_update()
    {
        await using var app = await StartManagementAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/routes", JsonText("{"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/routes", JsonText("null"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/routes/coding", RouteBody("other"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/routes/missing", RouteBody("missing"))).StatusCode);
    }

    [Fact]
    public async Task Empty_bearer_token_is_rejected()
    {
        await using var app = await StartManagementAsync(adminKey: "secret");
        var request = new HttpRequestMessage(HttpMethod.Get, "/providers");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer ");

        var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task OpenAi_endpoint_rejects_malformed_json_and_non_string_model()
    {
        await using var app = await StartOpenAiAsync(new StaticRouter(() => Success()));
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/chat/completions", JsonText("{"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/chat/completions", new { model = 7, messages = Array.Empty<object>() })).StatusCode);
    }

    [Theory]
    [InlineData(ProviderFailureKind.InvalidRequest, 400, "invalid_request_error")]
    [InlineData(ProviderFailureKind.RateLimited, 429, "rate_limit_error")]
    [InlineData(ProviderFailureKind.ProviderFailure, 0, "server_error")]
    public async Task OpenAi_failure_envelope_maps_failure_kind_and_default_status(ProviderFailureKind kind, int status, string expectedType)
    {
        var router = new StaticRouter(() => new RouterResult { Success = false, StatusCode = status, FailureKind = kind });
        await using var app = await StartOpenAiAsync(router);

        var response = await app.GetTestClient().PostAsJsonAsync("/v1/chat/completions", new { model = "m", messages = Array.Empty<object>() });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status > 0 ? status : 500, (int)response.StatusCode);
        Assert.Equal(expectedType, body.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetProperty("message").GetString()));
    }

    [Fact]
    public async Task OpenAi_success_without_body_headers_or_status_uses_defaults()
    {
        await using var app = await StartOpenAiAsync(new StaticRouter(() => new RouterResult { Success = true, StatusCode = 0 }));
        var response = await app.GetTestClient().PostAsJsonAsync("/v1/chat/completions", new { model = "m", messages = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-AiRouter-Provider"));
        Assert.False(response.Headers.Contains("X-AiRouter-Model"));
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenAi_stream_without_content_type_uses_sse_default()
    {
        var router = new StaticRouter(() => new RouterResult
        {
            Success = true,
            StatusCode = 200,
            Stream = new MemoryStream(Encoding.UTF8.GetBytes("data: ok\n\n"))
        });
        await using var app = await StartOpenAiAsync(router);

        var response = await app.GetTestClient().PostAsJsonAsync("/v1/chat/completions", new { model = "m", stream = true, messages = Array.Empty<object>() });

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("data: ok\n\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Models_discovers_empty_provider_models_and_ignores_discovery_failure()
    {
        var definitions = new[]
        {
            new ProviderDefinition("good", "Good", "fake", "https://unused.test", null, Models: [], DiscoverModels: true),
            new ProviderDefinition("bad", "Bad", "fake", "https://unused.test", null, Models: [], DiscoverModels: true)
        };
        var manager = new DiscoveryManager(definitions);
        await using var app = await StartOpenAiAsync(new StaticRouter(() => Success()), manager);

        var response = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/v1/models");
        var ids = response.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray();

        Assert.Contains("good/discovered", ids);
        Assert.Contains("all", ids);
        Assert.DoesNotContain("bad/discovered", ids);
    }

    private static object ProviderBody(string id, string baseUrl) => new
    {
        id,
        name = id,
        type = "fake",
        baseUrl,
        apiKey = (string?)null,
        enabled = true,
        models = new[] { "model-a" },
        defaultModel = "model-a",
        discoverModels = false,
        supportsNativeResponses = true
    };

    private static object RouteBody(string id) => new
    {
        id,
        strategy = 0,
        targets = new[] { new { providerId = "primary", model = "model-a", priority = 1, enabled = true } },
        enabled = true
    };

    private static StringContent JsonText(string text) => new(text, Encoding.UTF8, "application/json");

    private static async Task<WebApplication> StartManagementAsync(string? adminKey = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAiProviderFactory, FakeFactory>();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();
        var app = builder.Build();
        app.MapAiRouterManagementEndpoints(adminKey);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartOpenAiAsync(IAiRouter router, IProviderManager? providers = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(router);
        builder.Services.AddSingleton(providers ?? new DiscoveryManager([]));
        builder.Services.AddSingleton<IRouteStore>(new InMemoryRouteStore());
        builder.Services.AddAiRouterAspNetCore();
        var app = builder.Build();
        app.MapAiRouterOpenAiEndpoints();
        await app.StartAsync();
        return app;
    }

    private static RouterResult Success() => new() { Success = true, StatusCode = 200, Body = JsonSerializer.SerializeToElement(new { ok = true }) };

    private sealed class StaticRouter(Func<RouterResult> result) : IAiRouter
    {
        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Task.FromResult(result());
        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Task.FromResult(result());
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

    private sealed class DiscoveryManager(IReadOnlyList<ProviderDefinition> definitions) : IProviderManager
    {
        public IReadOnlyList<IAiProvider> Snapshot => [];
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => Task.FromResult(definitions);
        public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(definitions.FirstOrDefault(x => x.Id == id));
        public Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default) =>
            id == "bad" ? throw new InvalidOperationException("discovery failed") : Task.FromResult<IReadOnlyList<string>>(["discovered"]);
    }
}
