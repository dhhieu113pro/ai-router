using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AiRouter.Models;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiBranchCoverageTests
{
    private static readonly JsonElement Request = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Model_discovery_fallbacks_cover_null_models_bad_shape_and_transport_failure()
    {
        var unavailable = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), models: null);
        Assert.Empty(await unavailable.ListModelsAsync());

        var badShape = Create(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"data\":{}}"))), models: null);
        Assert.Empty(await badShape.ListModelsAsync());

        var missingData = Create(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"))), models: null);
        Assert.Empty(await missingData.ListModelsAsync());

        var broken = Create(new Handler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))), models: null);
        Assert.Empty(await broken.ListModelsAsync());
    }

    [Fact]
    public async Task Empty_success_body_and_base_url_without_trailing_slash_are_supported()
    {
        Uri? requested = null;
        var provider = Create(new Handler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
        }), baseUrl: "https://upstream.test/v1");

        var response = await provider.SendChatAsync("model", Request, false);

        Assert.True(response.Success);
        Assert.Null(response.Body);
        Assert.Equal("https://upstream.test/v1/chat/completions", requested!.AbsoluteUri);
    }

    [Fact]
    public async Task Empty_upstream_error_without_reason_uses_default_message()
    {
        var provider = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)599)
        {
            ReasonPhrase = null,
            Content = new StringContent(string.Empty)
        })));

        var response = await provider.SendChatAsync("model", Request, false);

        Assert.False(response.Success);
        Assert.Equal("Upstream request failed.", response.ErrorMessage);
    }

    [Fact]
    public async Task Streaming_response_dispose_false_branch_keeps_owned_resources_until_real_disposal()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("data: ok\n\n"));
        var provider = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(inner)
        })));
        var response = await provider.SendChatAsync("model", Request, true);
        Assert.NotNull(response.Stream);

        var dispose = response.Stream!.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null)!;
        dispose.Invoke(response.Stream, [false]);
        Assert.True(inner.CanRead);

        await response.Stream.DisposeAsync();
        Assert.False(inner.CanRead);
    }

    [Fact]
    public void Responses_translation_handles_non_string_identity_null_tools_and_out_of_range_usage()
    {
        var translator = new OpenAiResponsesTranslator();
        var input = JsonDocument.Parse("\"hello\"").RootElement.Clone();
        var nullTools = JsonDocument.Parse("null").RootElement.Clone();
        var request = new ResponsesRequest { Model = "fallback", Input = input, Tools = nullTools };
        Assert.Null(translator.ToChatRequest(request).Tools);

        var chat = JsonDocument.Parse("""
        {
          "id": 7,
          "model": 9,
          "choices": [{"message":{"content":{"value":1}}}],
          "usage": {"prompt_tokens": 2147483648, "completion_tokens": "bad", "total_tokens": 3}
        }
        """).RootElement.Clone();

        var translated = translator.ToResponsesResponse(chat, "fallback");

        Assert.StartsWith("resp_", translated.GetProperty("id").GetString());
        Assert.Equal("fallback", translated.GetProperty("model").GetString());
        Assert.Equal("{\"value\":1}", translated.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        var usage = translated.GetProperty("usage");
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("input_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("output_tokens").ValueKind);
        Assert.Equal(3, usage.GetProperty("total_tokens").GetInt32());
    }

    private static OpenAiCompatibleProvider Create(HttpMessageHandler handler, IReadOnlyList<string>? models = null, string baseUrl = "https://upstream.test/v1/") =>
        new(
            new ProviderDefinition("primary", "Primary", "openai-compatible", baseUrl, null, Models: models),
            new Factory(new HttpClient(handler)));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
