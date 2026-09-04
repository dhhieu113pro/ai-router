namespace AiRouter.Routing;

public sealed class InMemoryRouteStore : IRouteStore
{
    public Task<IReadOnlyList<RouteDefinition>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<RouteDefinition?> GetAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UpsertAsync(RouteDefinition route, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
}
