using System.Net;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiResidualCoverageTests
{
    private static readonly JsonElement Request = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Caller_cancellation_during_send_is_rethrown()
    {
        using var cts = new CancellationTokenSource();
        var handler = new Handler((_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
        });
        var provider = Create(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.SendChatAsync("model", Request, false, cts.Token));
    }

    [Fact]
    public async Task Model_discovery_awaits_asynchronous_stream_disposal()
    {
        var stream = new AsyncDisposeMemoryStream(Encoding.UTF8.GetBytes("{\"data\":[{\"id\":\"model-a\"}]}"));
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DirectStreamContent(stream)
        }));
        var provider = Create(handler);

        var models = await provider.ListModelsAsync();

        Assert.Equal(["model-a"], models);
        Assert.True(stream.DisposeAsyncCalled);
    }

    private static OpenAiCompatibleProvider Create(HttpMessageHandler handler) =>
        new(
            new ProviderDefinition("primary", "Primary", "openai-compatible", "https://upstream.test/v1/", null),
            new StaticFactory(new HttpClient(handler)));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StaticFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, cancellationToken);
    }

    private sealed class DirectStreamContent(Stream stream) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => stream.CopyToAsync(target);
        protected override bool TryComputeLength(out long length)
        {
            length = stream.Length;
            return true;
        }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult(stream);
    }

    private sealed class AsyncDisposeMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool DisposeAsyncCalled { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeAsyncCalled = true;
            Dispose();
        }
    }
}
