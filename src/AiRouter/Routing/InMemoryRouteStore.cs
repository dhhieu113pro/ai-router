using System.Collections.Concurrent;

namespace AiRouter.Routing;

public sealed class InMemoryRouteStore : IRouteStore
{
    private readonly ConcurrentDictionary<string, RouteDefinition> _routes = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<RouteDefinition>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<RouteDefinition> result = _routes.Values
            .OrderBy(static route => route.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<RouteDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _routes.TryGetValue(id, out var route);
        return Task.FromResult(route);
    }

    public Task UpsertAsync(RouteDefinition route, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _routes[route.Id] = route;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _routes.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
