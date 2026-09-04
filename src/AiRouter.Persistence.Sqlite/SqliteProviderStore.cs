using System.Text.Json;
using AiRouter.Providers;
using Microsoft.EntityFrameworkCore;

namespace AiRouter.Persistence.Sqlite;

public sealed class SqliteProviderStore(SqliteStoreOptions options) : IProviderStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var entities = await db.Providers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
        return entities.Select(Map).ToArray();
    }

    public async Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var entity = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task UpsertAsync(ProviderDefinition provider, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var entity = await db.Providers.SingleOrDefaultAsync(x => x.Id == provider.Id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new ProviderEntity { Id = provider.Id };
            db.Providers.Add(entity);
        }

        Apply(provider, entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var entity = await db.Providers.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return;
        db.Providers.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static void Apply(ProviderDefinition source, ProviderEntity target)
    {
        target.Name = source.Name;
        target.Type = source.Type;
        target.BaseUrl = source.BaseUrl;
        target.ApiKey = source.ApiKey;
        target.Enabled = source.Enabled;
        target.Priority = source.Priority;
        target.TimeoutMilliseconds = source.Timeout is null ? null : checked((long)source.Timeout.Value.TotalMilliseconds);
        target.ModelsJson = JsonSerializer.Serialize(source.Models ?? [], Json);
        target.DefaultModel = source.DefaultModel;
        target.DiscoverModels = source.DiscoverModels;
        target.ExtraHeadersJson = JsonSerializer.Serialize(source.ExtraHeaders ?? new Dictionary<string, string>(), Json);
        target.ChatEndpoint = source.ChatEndpoint;
        target.ResponsesEndpoint = source.ResponsesEndpoint;
        target.ModelsEndpoint = source.ModelsEndpoint;
        target.SupportsNativeResponses = source.SupportsNativeResponses;
    }

    private static ProviderDefinition Map(ProviderEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.Type,
        entity.BaseUrl,
        entity.ApiKey,
        entity.Enabled,
        entity.Priority,
        entity.TimeoutMilliseconds is null ? null : TimeSpan.FromMilliseconds(entity.TimeoutMilliseconds.Value),
        JsonSerializer.Deserialize<List<string>>(entity.ModelsJson, Json) ?? [],
        entity.DefaultModel,
        entity.DiscoverModels,
        JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ExtraHeadersJson, Json) ?? new Dictionary<string, string>(),
        entity.ChatEndpoint,
        entity.ResponsesEndpoint,
        entity.ModelsEndpoint,
        entity.SupportsNativeResponses);
}
