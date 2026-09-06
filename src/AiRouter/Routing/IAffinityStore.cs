namespace AiRouter.Routing;

public sealed record AffinityEntry(
    string ProviderId,
    string Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt);

public interface IAffinityStore
{
    bool TryGet(string routeId, string affinityKey, DateTimeOffset now, out AffinityEntry entry);
    void Set(string routeId, string affinityKey, ResolvedTarget target, DateTimeOffset now, TimeSpan ttl);
}
