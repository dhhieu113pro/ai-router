using AiRouter.Providers;

namespace AiRouter.Providers.OpenAI;

public sealed class OpenAiCompatibleProviderFactory(IHttpClientFactory httpClientFactory) : IAiProviderFactory
{
    public bool CanCreate(ProviderDefinition definition) =>
        string.Equals(definition.Type, "openai-compatible", StringComparison.OrdinalIgnoreCase);

    public IAiProvider Create(ProviderDefinition definition) =>
        new OpenAiCompatibleProvider(definition, httpClientFactory);
}
