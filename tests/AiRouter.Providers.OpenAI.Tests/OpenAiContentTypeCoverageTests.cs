using System.Net;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiContentTypeCoverageTests
{
    private static readonly JsonElement Request = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Successful_response_without_content_type_returns_null_content_type()
    {
        var provider = new OpenAiCompatibleProvider(
            new ProviderDefinition(
                "primary",
                "Primary",
                "openai-compatible",
                "https://upstream.test/v1/",
                null,
                Models: ["model"]),
            new Factory(new HttpClient(new Handler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                })))));

        var response = await provider.SendChatAsync("model", Request, false);

        Assert.True(response.Success);
        Assert.Null(response.Body);
        Assert.Null(response.ContentType);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
