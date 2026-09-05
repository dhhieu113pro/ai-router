namespace AiRouter.Providers;

public interface IProviderManager
{
    IReadOnlyList<IAiProvider> Snapshot { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default);
    Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default);
    Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);
    Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default);
}
