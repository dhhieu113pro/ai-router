using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Routing;

public sealed class RouterResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
    public JsonElement? Body { get; init; }
    public Stream? Stream { get; init; }
    public string? ContentType { get; init; }
    public string? ErrorMessage { get; init; }
    public ProviderFailureKind FailureKind { get; init; }
    public bool AffinityApplied { get; init; }
    public string AffinitySource { get; init; } = "route";
    public bool AffinityRebound { get; init; }
    public bool FallbackOccurred { get; init; }
    public int AttemptCount { get; init; }
    public string AffinityClassification { get; init; } = "route";
}

public interface IAiRouter
{
    Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default);
    Task<RouterResult> ChatAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default);
    Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default);
    Task<RouterResult> ResponsesAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default);
}
