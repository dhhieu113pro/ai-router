using System.Net;
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

public sealed class EmbeddedHostingTests
{
    [Fact]
    public async Task Chat_endpoint_uses_the_exact_registered_router_instance()
    {
        var router = new RecordingRouter();
        await using var app = await StartAsync(router);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "coding",
            messages = new[] { new { role = "user", content = "hello" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("primary", response.Headers.GetValues("X-AiRouter-Provider").Single());
        Assert.Equal("model-a", response.Headers.GetValues("X-AiRouter-Model").Single());
        Assert.Equal(1, router.ChatCalls);
        Assert.Equal("coding", router.LastModel);
        Assert.Equal("coding", router.LastBody!.Value.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Responses_endpoint_uses_the_same_router()
    {
        var router = new RecordingRouter();
        await using var app = await StartAsync(router);
        var response = await app.GetTestClient().PostAsJsonAsync("/v1/responses", new
        {
            model = "coding",
            input = "hello"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, router.ResponsesCalls);
        Assert.Equal("coding", router.LastModel);
    }

    [Fact]
    public async Task Streaming_response_is_forwarded_as_sse()
    {
        var router = new RecordingRouter { StreamNext = true };
        await using var app = await StartAsync(router);
        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "coding",
                stream = true,
                messages = new[] { new { role = "user", content = "hello" } }
            })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("data: {\"ok\":true}\n\n", await response.Content.ReadAsStringAsync());
        Assert.True(router.LastStream);
    }

    [Fact]
    public async Task Missing_model_returns_openai_style_validation_error()
    {
        var router = new RecordingRouter();
        await using var app = await StartAsync(router);
        var response = await app.GetTestClient().PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "hello" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request_error", body.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal(0, router.ChatCalls);
    }

    [Fact]
    public async Task Router_failure_preserves_status_and_openai_error_envelope()
    {
        var router = new RecordingRouter { FailNext = true };
        await using var app = await StartAsync(router);
        var response = await app.GetTestClient().PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "coding",
            messages = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("provider down", body.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Models_endpoint_returns_logical_routes_and_direct_provider_models()
    {
        var manager = new StubProviderManager([
            new ProviderDefinition("primary", "Primary", "fake", "https://unused.test", null, Models: ["model-a"], DefaultModel: "model-a")
        ]);
        var routes = new InMemoryRouteStore();
        await routes.UpsertAsync(new RouteDefinition("coding", RoutingStrategy.Fallback, [new RouteTarget("primary", "model-a")]));

        await using var app = await StartAsync(new RecordingRouter(), manager, routes);
        var response = await app.GetTestClient().GetAsync("/v1/models");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = body.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("coding", ids);
        Assert.Contains("primary/model-a", ids);
    }

    [Fact]
    public async Task AddAiRouter_respects_a_pre_registered_custom_router()
    {
        var router = new RecordingRouter();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAiRouter>(router);
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();

        await using var app = builder.Build();
        Assert.Same(router, app.Services.GetRequiredService<IAiRouter>());
    }

    private static async Task<WebApplication> StartAsync(
        IAiRouter router,
        IProviderManager? providers = null,
        IRouteStore? routes = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(router);
        builder.Services.AddSingleton(providers ?? new StubProviderManager([]));
        builder.Services.AddSingleton(routes ?? new InMemoryRouteStore());
        builder.Services.AddAiRouterAspNetCore();

        var app = builder.Build();
        app.MapAiRouterOpenAiEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingRouter : IAiRouter
    {
        public int ChatCalls { get; private set; }
        public int ResponsesCalls { get; private set; }
        public string? LastModel { get; private set; }
        public JsonElement? LastBody { get; private set; }
        public bool LastStream { get; private set; }
        public bool StreamNext { get; init; }
        public bool FailNext { get; init; }

        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default)
        {
            ChatCalls++;
            Capture(model, body, stream);
            return Task.FromResult(Result(stream));
        }

        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default)
        {
            ResponsesCalls++;
            Capture(model, body, stream);
            return Task.FromResult(Result(stream));
        }

        private void Capture(string model, JsonElement body, bool stream)
        {
            LastModel = model;
            LastBody = body.Clone();
            LastStream = stream;
        }

        private RouterResult Result(bool stream)
        {
            if (FailNext)
                return new RouterResult
                {
                    Success = false,
                    StatusCode = 503,
                    FailureKind = ProviderFailureKind.ProviderFailure,
                    ErrorMessage = "provider down"
                };

            if (StreamNext || stream)
                return new RouterResult
                {
                    Success = true,
                    StatusCode = 200,
                    ProviderId = "primary",
                    Model = "model-a",
                    ContentType = "text/event-stream",
                    Stream = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"ok\":true}\n\n"))
                };

            return new RouterResult
            {
                Success = true,
                StatusCode = 200,
                ProviderId = "primary",
                Model = "model-a",
                Body = JsonSerializer.SerializeToElement(new { ok = true })
            };
        }
    }

    private sealed class StubProviderManager(IReadOnlyList<ProviderDefinition> definitions) : IProviderManager
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
            Task.FromResult<IReadOnlyList<string>>(definitions.FirstOrDefault(x => x.Id == id)?.Models ?? []);
    }
}
