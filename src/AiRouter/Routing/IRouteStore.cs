namespace AiRouter.Routing;

public interface IRouteStore
{
    Task<IReadOnlyList<RouteDefinition>> ListAsync(CancellationToken ct = default);
    Task<RouteDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(RouteDefinition route, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
