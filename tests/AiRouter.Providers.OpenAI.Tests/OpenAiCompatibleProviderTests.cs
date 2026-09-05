using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiCompatibleProviderTests
{
    private static readonly JsonElement Request = JsonDocument.Parse("{\"model\":\"route\",\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Chat_uses_selected_model_endpoint_auth_and_extra_headers()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"id\":\"ok\"}"));
        var provider = Create(handler, new ProviderDefinition(
            "primary", "Primary", "openai-compatible", "https://upstream.test/v1/", "secret",
            ExtraHeaders: new Dictionary<string, string> { ["X-Tenant"] = "alpha" },
            ChatEndpoint: "chat/custom"));

        var result = await provider.SendChatAsync("actual-model", Request, false);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://upstream.test/v1/chat/custom", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "secret"), handler.LastRequest.Headers.Authorization);
        Assert.Equal("alpha", handler.LastRequest.Headers.GetValues("X-Tenant").Single());
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        Assert.Equal("actual-model", body.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Streaming_chat_returns_upstream_stream_without_buffering_contract_change()
    {
        const string sse = "data: {\"choices\":[]}\n\ndata: [DONE]\n\n";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse)))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") }
            }
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model-a", Request, true);

        Assert.True(result.Success);
        Assert.NotNull(result.Stream);
        Assert.Equal("text/event-stream", result.ContentType);
        using var reader = new StreamReader(result.Stream!);
        Assert.Equal(sse, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task List_models_reads_openai_data_array()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":[{\"id\":\"m2\"},{\"id\":\"m1\"}]}"));
        var provider = Create(handler);

        var models = await provider.ListModelsAsync();

        Assert.Equal(["m1", "m2"], models);
        Assert.Equal("https://upstream.test/v1/models", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Health_check_reports_success_for_reachable_models_endpoint()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        var provider = Create(handler);

        var result = await provider.CheckHealthAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.Latency);
    }

    [Theory]
    [InlineData(400, ProviderFailureKind.InvalidRequest)]
    [InlineData(422, ProviderFailureKind.InvalidRequest)]
    [InlineData(404, ProviderFailureKind.TargetFailure)]
    [InlineData(401, ProviderFailureKind.ProviderFailure)]
    [InlineData(403, ProviderFailureKind.ProviderFailure)]
    [InlineData(408, ProviderFailureKind.ProviderFailure)]
    [InlineData(409, ProviderFailureKind.ProviderFailure)]
    [InlineData(429, ProviderFailureKind.RateLimited)]
    [InlineData(500, ProviderFailureKind.ProviderFailure)]
    public async Task Chat_classifies_upstream_errors(int status, ProviderFailureKind expected)
    {
        var handler = new RecordingHandler(_ => Json((HttpStatusCode)status, "{\"error\":{\"message\":\"failed\"}}"));
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model-a", Request, false);

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureKind);
        Assert.Equal(status, result.StatusCode);
        Assert.Contains("failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        var provider = Create(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SendChatAsync("model-a", Request, false, cts.Token));
    }

    private static OpenAiCompatibleProvider Create(RecordingHandler handler, ProviderDefinition? definition = null)
    {
        definition ??= new ProviderDefinition("primary", "Primary", "openai-compatible", "https://upstream.test/v1/", "secret");
        return new OpenAiCompatibleProvider(definition, new StaticHttpClientFactory(new HttpClient(handler)));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) :
            this((request, _) => Task.FromResult(response(request))) { }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return await _response(request, cancellationToken);
        }
    }
}
