namespace AiRouter.Providers;

public interface IProviderStore
{
    Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default);
    Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(ProviderDefinition provider, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
