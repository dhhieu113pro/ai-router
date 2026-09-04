namespace AiRouter.Providers;

public sealed class InMemoryProviderStore : IProviderStore
{
    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UpsertAsync(ProviderDefinition provider, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
}
