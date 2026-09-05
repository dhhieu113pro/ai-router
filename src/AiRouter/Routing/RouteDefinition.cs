namespace AiRouter.Routing;

public sealed record RouteTarget(string ProviderId, string Model, int Priority = 100, bool Enabled = true);
public sealed record RouteDefinition(string Id, RoutingStrategy Strategy, IReadOnlyList<RouteTarget> Targets, bool Enabled = true);
public sealed record ResolvedTarget(string ProviderId, string Model);
public sealed record ResolvedRoute(string RouteId, RoutingStrategy Strategy, bool Pinned, IReadOnlyList<ResolvedTarget> Targets);

public sealed class RouteResolutionException(string message) : Exception(message)
{
    public string ErrorType { get; } = "invalid_request_error";
}
