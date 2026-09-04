namespace AiRouter.Providers;

public sealed record ProviderDefinition(
    string Id,
    string Name,
    string Type,
    string BaseUrl,
    string? ApiKey,
    bool Enabled = true,
    int Priority = 100,
    TimeSpan? Timeout = null,
    IReadOnlyList<string>? Models = null,
    string? DefaultModel = null,
    bool DiscoverModels = true,
    IReadOnlyDictionary<string, string>? ExtraHeaders = null,
    string? ChatEndpoint = null,
    string? ResponsesEndpoint = null,
    string? ModelsEndpoint = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(120);
    public ProviderDefinition Redacted() => this with { ApiKey = null };
}
