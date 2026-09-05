using System.Text.Json;

namespace AiRouter.Providers;

public interface IAiProvider
{
    ProviderDefinition Definition { get; }
    ProviderHealth Health { get; }
    Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default);
    Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
    Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default);
}
