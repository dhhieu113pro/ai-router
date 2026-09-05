using AiRouter.Routing;
using Microsoft.EntityFrameworkCore;

namespace AiRouter.Persistence.Sqlite;

public sealed class SqliteRouteStore(SqliteStoreOptions options) : IRouteStore
{
    public async Task<IReadOnlyList<RouteDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var routes = await db.Routes.AsNoTracking().Include(x => x.Targets).OrderBy(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
        return routes.Select(Map).ToArray();
    }

    public async Task<RouteDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var route = await db.Routes.AsNoTracking().Include(x => x.Targets).SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return route is null ? null : Map(route);
    }

    public async Task UpsertAsync(RouteDefinition route, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var entity = await db.Routes.Include(x => x.Targets).SingleOrDefaultAsync(x => x.Id == route.Id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new RouteEntity { Id = route.Id };
            db.Routes.Add(entity);
        }
        else if (entity.Targets.Count > 0)
        {
            db.RouteTargets.RemoveRange(entity.Targets);
            entity.Targets.Clear();
        }

        entity.Strategy = (int)route.Strategy;
        entity.Enabled = route.Enabled;
        entity.Targets = route.Targets.Select((target, ordinal) => new RouteTargetEntity
        {
            RouteId = route.Id,
            Ordinal = ordinal,
            ProviderId = target.ProviderId,
            Model = target.Model,
            Priority = target.Priority,
            Enabled = target.Enabled
        }).ToList();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await SqliteContextFactory.CreateInitializedAsync(options, ct).ConfigureAwait(false);
        var route = await db.Routes.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (route is null) return;
        db.Routes.Remove(route);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static RouteDefinition Map(RouteEntity route) => new(
        route.Id,
        (RoutingStrategy)route.Strategy,
        route.Targets.OrderBy(x => x.Ordinal).Select(x => new RouteTarget(x.ProviderId, x.Model, x.Priority, x.Enabled)).ToArray(),
        route.Enabled);
}
