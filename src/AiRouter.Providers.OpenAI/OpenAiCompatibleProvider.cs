using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Providers.OpenAI;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    public OpenAiCompatibleProvider(ProviderDefinition definition, IHttpClientFactory httpClientFactory)
    {
        Definition = definition;
    }

    public ProviderDefinition Definition { get; }
    public ProviderHealth Health { get; } = new();

    public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();
}
