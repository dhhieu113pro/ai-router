using System.Text.Json;

namespace AiRouter.Providers;

public enum ProviderFailureKind
{
    None,
    InvalidRequest,
    TargetFailure,
    ProviderFailure,
    RateLimited,
    Cancelled
}

public sealed class ProviderResponse
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public ProviderFailureKind FailureKind { get; init; }
    public JsonElement? Body { get; init; }
    public Stream? Stream { get; init; }
    public string? ContentType { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? RetryAfter { get; init; }
    public bool StreamCommitted { get; init; }

    public static ProviderResponse Failed(ProviderFailureKind kind, int statusCode, string message) =>
        new() { FailureKind = kind, StatusCode = statusCode, ErrorMessage = message };
}

public sealed record ProviderConnectivityResult(bool Success, string? Error = null, TimeSpan? Latency = null);
