using System.Collections.Concurrent;

namespace AiRouter.Providers;

public sealed class InMemoryProviderStore : IProviderStore
{
    private readonly ConcurrentDictionary<string, ProviderDefinition> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<ProviderDefinition> result = _providers.Values
            .OrderBy(static p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _providers.TryGetValue(id, out var provider);
        return Task.FromResult(provider);
    }

    public Task UpsertAsync(ProviderDefinition provider, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _providers[provider.Id] = provider;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _providers.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
