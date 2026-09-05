using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiTransportCoverageEdgeTests
{
    private static readonly JsonElement Request = JsonDocument.Parse("{\"messages\":[]}").RootElement.Clone();

    [Fact]
    public async Task Responses_uses_absolute_override_without_authorization_and_rewrites_body()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var definition = Definition() with
        {
            BaseUrl = "https://upstream.test/v1",
            ApiKey = null,
            ResponsesEndpoint = "https://responses.test/custom"
        };
        var provider = Create(handler, definition);

        var result = await provider.SendResponsesAsync("actual", Request, true);

        Assert.True(result.Success);
        Assert.Equal("https://responses.test/custom", handler.LastRequest!.RequestUri!.ToString());
        Assert.Null(handler.LastRequest.Headers.Authorization);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("actual", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Non_object_request_body_is_rewritten_as_object()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var provider = Create(handler);
        var scalar = JsonDocument.Parse("42").RootElement.Clone();

        var result = await provider.SendChatAsync("model-x", scalar, false);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("model-x", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task List_models_non_success_returns_configured_models()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var provider = Create(handler, Definition() with { Models = ["configured"] });

        Assert.Equal(["configured"], await provider.ListModelsAsync());
    }

    [Fact]
    public async Task List_models_invalid_shape_returns_configured_models()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));
        var provider = Create(handler, Definition() with { Models = ["configured"] });

        Assert.Equal(["configured"], await provider.ListModelsAsync());
    }

    [Fact]
    public async Task List_models_malformed_json_returns_configured_models()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        var provider = Create(handler, Definition() with { Models = ["configured"] });

        Assert.Equal(["configured"], await provider.ListModelsAsync());
    }

    [Fact]
    public async Task List_models_network_exception_returns_configured_models()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("network"));
        var provider = Create(handler, Definition() with { Models = ["configured"] });

        Assert.Equal(["configured"], await provider.ListModelsAsync());
    }

    [Fact]
    public async Task List_models_caller_cancellation_propagates()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        var provider = Create(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ListModelsAsync(cts.Token));
    }

    [Fact]
    public async Task List_models_filters_bad_entries_duplicates_and_blank_ids()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":[null,{}, {\"id\":1},{\"id\":\"\"},{\"id\":\"M\"},{\"id\":\"m\"}]}"));
        var provider = Create(handler);

        Assert.Equal(["M"], await provider.ListModelsAsync());
    }

    [Fact]
    public async Task Health_reports_non_success_status()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.BadGateway, "{}"));
        var provider = Create(handler);

        var health = await provider.CheckHealthAsync();

        Assert.False(health.Success);
        Assert.Contains("502", health.Error, StringComparison.Ordinal);
        Assert.NotNull(health.Latency);
    }

    [Fact]
    public async Task Health_network_exception_is_reported()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("offline"));
        var provider = Create(handler);

        var health = await provider.CheckHealthAsync();

        Assert.False(health.Success);
        Assert.Contains("offline", health.Error, StringComparison.Ordinal);
        Assert.NotNull(health.Latency);
    }

    [Fact]
    public async Task Health_caller_cancellation_propagates()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        var provider = Create(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CheckHealthAsync(cts.Token));
    }

    [Fact]
    public async Task Provider_timeout_is_reported_as_504()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        var provider = Create(handler, Definition() with { Timeout = TimeSpan.FromMilliseconds(10) });

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.False(result.Success);
        Assert.Equal(504, result.StatusCode);
        Assert.Equal(ProviderFailureKind.ProviderFailure, result.FailureKind);
    }

    [Fact]
    public async Task Http_request_exception_is_reported_as_503()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("network"));
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("network", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_string_and_retry_after_date_are_preserved()
    {
        var retry = DateTimeOffset.UtcNow.AddMinutes(1);
        var handler = new RecordingHandler(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, "{\"error\":\"busy\"}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retry);
            return response;
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.Equal("busy", result.ErrorMessage);
        Assert.NotNull(result.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_delta_is_converted_to_date()
    {
        var before = DateTimeOffset.UtcNow;
        var handler = new RecordingHandler(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter >= before.AddSeconds(25));
    }

    [Fact]
    public async Task Invalid_json_error_body_is_returned_verbatim()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("raw upstream failure")
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.Equal("raw upstream failure", result.ErrorMessage);
    }

    [Fact]
    public async Task Empty_error_body_falls_back_to_reason_phrase()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Custom reason",
            Content = new StringContent(string.Empty)
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.Equal("Custom reason", result.ErrorMessage);
    }

    [Fact]
    public async Task Unknown_error_status_uses_provider_failure_classification()
    {
        var handler = new RecordingHandler(_ => Json((HttpStatusCode)418, "{}"));
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.Equal(ProviderFailureKind.ProviderFailure, result.FailureKind);
    }

    [Fact]
    public async Task Empty_success_body_returns_null_body()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new StringContent(string.Empty)
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, false);

        Assert.True(result.Success);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task Streaming_result_delegates_full_stream_surface_and_disposes_async()
    {
        var inner = new MemoryStream(new byte[64], writable: true);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(inner)
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, true);
        var stream = Assert.IsAssignableFrom<Stream>(result.Stream);

        Assert.Equal("text/event-stream", result.ContentType);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        Assert.Equal(64, stream.Length);
        stream.Position = 0;
        Assert.Equal(0, stream.Position);
        var buffer = new byte[4];
        Assert.Equal(4, stream.Read(buffer, 0, buffer.Length));
        stream.Position = 0;
        Assert.Equal(4, stream.Read(buffer.AsSpan()));
        stream.Position = 0;
        Assert.Equal(4, await stream.ReadAsync(buffer.AsMemory()));
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        stream.SetLength(32);
        stream.Position = 0;
        stream.Write(new byte[] { 1, 2 }, 0, 2);
        stream.Write(new byte[] { 3, 4 }.AsSpan());
        stream.Flush();
        await stream.FlushAsync();
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task Streaming_result_supports_synchronous_disposal()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(inner)
        });
        var provider = Create(handler);

        var result = await provider.SendChatAsync("model", Request, true);
        result.Stream!.Dispose();

        Assert.Throws<ObjectDisposedException>(() => inner.ReadByte());
    }

    [Fact]
    public async Task Streaming_content_open_failure_is_rethrown_after_response_cleanup()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingReadStreamContent()
        });
        var provider = Create(handler);

        await Assert.ThrowsAsync<IOException>(() => provider.SendChatAsync("model", Request, true));
    }

    private static ProviderDefinition Definition() =>
        new("primary", "Primary", "openai-compatible", "https://upstream.test/v1/", "secret");

    private static OpenAiCompatibleProvider Create(RecordingHandler handler, ProviderDefinition? definition = null) =>
        new(definition ?? Definition(), new StaticHttpClientFactory(new HttpClient(handler)));

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

    private sealed class ThrowingReadStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromException<Stream>(new IOException("stream open failed"));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromException<Stream>(new IOException("stream open failed"));
    }
}
