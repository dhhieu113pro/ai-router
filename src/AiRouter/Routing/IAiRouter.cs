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
}

public interface IAiRouter
{
    Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default);
    Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default);
}
