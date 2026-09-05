using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRouter.Providers;

namespace AiRouter.Providers.OpenAI;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAiCompatibleProvider(ProviderDefinition definition, IHttpClientFactory httpClientFactory)
    {
        Definition = definition;
        _httpClientFactory = httpClientFactory;
    }

    public ProviderDefinition Definition { get; }
    public ProviderHealth Health { get; } = new();

    public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
        SendJsonAsync(model, requestBody, stream, Definition.ChatEndpoint, "chat/completions", ct);

    public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
        SendJsonAsync(model, requestBody, stream, Definition.ResponsesEndpoint, "responses", ct);

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, ResolveEndpoint(Definition.ModelsEndpoint, "models"));
        using var timeout = CreateTimeoutToken(ct);
        try
        {
            using var response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Definition.Models ?? [];

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            try
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return Definition.Models ?? [];

                return data.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetProperty("id").GetString())
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Select(static id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Definition.Models ?? [];
        }
    }

    public async Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, ResolveEndpoint(Definition.ModelsEndpoint, "models"));
        using var timeout = CreateTimeoutToken(ct);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var latency = Stopwatch.GetElapsedTime(started);
            return response.IsSuccessStatusCode
                ? new ProviderConnectivityResult(true, Latency: latency)
                : new ProviderConnectivityResult(false, $"Upstream returned {(int)response.StatusCode}.", latency);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProviderConnectivityResult(false, ex.Message, Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task<ProviderResponse> SendJsonAsync(
        string model,
        JsonElement requestBody,
        bool stream,
        string? endpointOverride,
        string defaultEndpoint,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var request = CreateRequest(HttpMethod.Post, ResolveEndpoint(endpointOverride, defaultEndpoint));
        request.Content = new StringContent(RewriteBody(requestBody, model, stream), Encoding.UTF8, "application/json");
        using var timeout = CreateTimeoutToken(ct);

        HttpResponseMessage response;
        try
        {
            response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 504, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, ex.Message);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeout.Token);
                return new ProviderResponse
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    FailureKind = OpenAiErrorClassifier.Classify((int)response.StatusCode),
                    ErrorMessage = ExtractError(errorBody, response.ReasonPhrase),
                    RetryAfter = GetRetryAfter(response)
                };
            }
        }

        if (stream)
        {
            try
            {
                var upstream = await response.Content.ReadAsStreamAsync(timeout.Token);
                return new ProviderResponse
                {
                    Success = true,
                    StatusCode = (int)response.StatusCode,
                    FailureKind = ProviderFailureKind.None,
                    Stream = new ResponseOwnedStream(upstream, response),
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "text/event-stream"
                };
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            JsonElement? body = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var document = JsonDocument.Parse(text);
                body = document.RootElement.Clone();
            }

            return new ProviderResponse
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                FailureKind = ProviderFailureKind.None,
                Body = body,
                ContentType = response.Content.Headers.ContentType?.MediaType
            };
        }
    }

    private HttpClient Client() => _httpClientFactory.CreateClient("AiRouter.OpenAICompatible");

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(Definition.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Definition.ApiKey);

        if (Definition.ExtraHeaders is not null)
        {
            foreach (var (name, value) in Definition.ExtraHeaders)
                request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private Uri ResolveEndpoint(string? configured, string fallback)
    {
        if (Uri.TryCreate(configured, UriKind.Absolute, out var absolute))
            return absolute;

        var baseUrl = Definition.BaseUrl.EndsWith('/') ? Definition.BaseUrl : Definition.BaseUrl + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), string.IsNullOrWhiteSpace(configured) ? fallback : configured.TrimStart('/'));
    }

    private static string RewriteBody(JsonElement body, string model, bool stream)
    {
        var node = JsonNode.Parse(body.GetRawText()) as JsonObject ?? new JsonObject();
        node["model"] = model;
        node["stream"] = stream;
        return node.ToJsonString();
    }

    private CancellationTokenSource CreateTimeoutToken(CancellationToken caller)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(caller);
        linked.CancelAfter(Definition.EffectiveTimeout);
        return linked;
    }

    private static string ExtractError(string text, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        return message.GetString()!;
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString()!;
                }
            }
            catch (JsonException) { }
            return text;
        }
        return fallback ?? "Upstream request failed.";
    }

    private static DateTimeOffset? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Date is { } date)
            return date;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return DateTimeOffset.UtcNow + delta;
        return null;
    }

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
