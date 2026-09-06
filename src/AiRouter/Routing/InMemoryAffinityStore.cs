using System.Collections.Concurrent;

namespace AiRouter.Routing;

public sealed class InMemoryAffinityStore : IAffinityStore
{
    private readonly ConcurrentDictionary<(string RouteId, string Key), AffinityEntry> _entries = new();

    public bool TryGet(string routeId, string affinityKey, DateTimeOffset now, out AffinityEntry entry)
    {
        var key = (routeId, affinityKey);
        if (!_entries.TryGetValue(key, out var current))
        {
            entry = default!;
            return false;
        }

        if (current.ExpiresAt <= now)
        {
            _entries.TryRemove(key, out _);
            entry = default!;
            return false;
        }

        var ttl = current.ExpiresAt - current.LastUsedAt;
        var refreshed = current with { LastUsedAt = now, ExpiresAt = now + ttl };
        _entries[key] = refreshed;
        entry = refreshed;
        return true;
    }

    public void Set(string routeId, string affinityKey, ResolvedTarget target, DateTimeOffset now, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(affinityKey);
        ArgumentNullException.ThrowIfNull(target);
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));

        _entries[(routeId, affinityKey)] = new AffinityEntry(
            target.ProviderId,
            target.Model,
            now,
            now,
            now + ttl);
    }
}
