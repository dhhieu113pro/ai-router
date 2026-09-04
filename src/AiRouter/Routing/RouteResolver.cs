using AiRouter.Providers;

namespace AiRouter.Routing;

public sealed class RouteResolver(IProviderManager providers, IRouteStore routes)
{
    public Task<ResolvedRoute> ResolveAsync(string model, CancellationToken ct = default) => throw new NotImplementedException();
}
