namespace AiRouter.Providers;

public sealed class ProviderManager : IProviderManager
{
    public ProviderManager(IProviderStore store, IEnumerable<IAiProviderFactory> factories)
    {
    }

    public IReadOnlyList<IAiProvider> Snapshot => Array.Empty<IAiProvider>();
    public Task InitializeAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
}
