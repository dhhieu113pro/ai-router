using System.Text.RegularExpressions;

namespace AiRouter.Providers;

public sealed class ProviderManager : IProviderManager
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly IProviderStore _store;
    private readonly IAiProviderFactory[] _factories;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAiProvider[] _snapshot = [];

    public ProviderManager(IProviderStore store, IEnumerable<IAiProviderFactory> factories)
    {
        _store = store;
        _factories = factories.ToArray();
    }

    public IReadOnlyList<IAiProvider> Snapshot => _snapshot;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await RebuildSnapshotAsync(ct); }
        finally { _gate.Release(); }
    }

    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => _store.ListAsync(ct);

    public Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => _store.GetAsync(id, ct);

    public async Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default)
    {
        Validate(provider);
        EnsureFactory(provider);
        await _gate.WaitAsync(ct);
        try
        {
            if (await _store.GetAsync(provider.Id, ct) is not null)
                throw new InvalidOperationException($"Provider '{provider.Id}' already exists.");

            await _store.UpsertAsync(provider, ct);
            await RebuildSnapshotAsync(ct);
            return provider;
        }
        finally { _gate.Release(); }
    }

    public async Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default)
    {
        if (!string.Equals(id, provider.Id, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provider id in the route must match the body id.", nameof(provider));

        Validate(provider);
        EnsureFactory(provider);
        await _gate.WaitAsync(ct);
        try
        {
            var existing = await _store.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Provider '{id}' was not found.");
            var updated = provider.ApiKey is null ? provider with { ApiKey = existing.ApiKey } : provider;
            await _store.UpsertAsync(updated, ct);
            await RebuildSnapshotAsync(ct);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await _store.DeleteAsync(id, ct);
            await RebuildSnapshotAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var existing = await _store.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Provider '{id}' was not found.");
            var updated = existing with { Enabled = enabled };
            await _store.UpsertAsync(updated, ct);
            await RebuildSnapshotAsync(ct);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default)
    {
        var provider = await ResolveRuntimeAsync(id, ct);
        return await provider.CheckHealthAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default)
    {
        var provider = await ResolveRuntimeAsync(id, ct);
        return await provider.ListModelsAsync(ct);
    }

    private async Task<IAiProvider> ResolveRuntimeAsync(string id, CancellationToken ct)
    {
        var current = _snapshot.FirstOrDefault(p => string.Equals(p.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
        if (current is not null) return current;

        var definition = await _store.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Provider '{id}' was not found.");
        return CreateProvider(definition);
    }

    private async Task RebuildSnapshotAsync(CancellationToken ct)
    {
        var definitions = await _store.ListAsync(ct);
        _snapshot = definitions
            .Where(static p => p.Enabled)
            .OrderBy(static p => p.Priority)
            .ThenBy(static p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(CreateProvider)
            .ToArray();
    }

    private IAiProvider CreateProvider(ProviderDefinition provider)
    {
        var factory = _factories.FirstOrDefault(f => f.CanCreate(provider))
            ?? throw new InvalidOperationException($"No provider factory is registered for type '{provider.Type}'.");
        return factory.Create(provider);
    }

    private void EnsureFactory(ProviderDefinition provider)
    {
        if (!_factories.Any(f => f.CanCreate(provider)))
            throw new InvalidOperationException($"No provider factory is registered for type '{provider.Type}'.");
    }

    private static void Validate(ProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Id) || !IdPattern.IsMatch(provider.Id))
            throw new ArgumentException("Provider id must start with an alphanumeric character and contain only letters, digits, '.', '_' or '-'.", nameof(provider));
        if (string.IsNullOrWhiteSpace(provider.Name))
            throw new ArgumentException("Provider name is required.", nameof(provider));
        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Provider BaseUrl must be an absolute HTTP or HTTPS URL.", nameof(provider));
    }
}
