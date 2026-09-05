using AiRouter.Providers;

namespace AiRouter.Routing;

public sealed class RouteResolver
{
    private readonly IProviderManager _providers;
    private readonly IRouteStore _routes;

    public RouteResolver(IProviderManager providers, IRouteStore routes)
    {
        _providers = providers;
        _routes = routes;
    }

    public async Task<ResolvedRoute> ResolveAsync(string model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = model?.Trim();
        if (string.IsNullOrEmpty(value))
            throw new RouteResolutionException("Model is required.");

        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            return await ResolveAllAsync(ct);

        var slash = value.IndexOf('/');
        if (slash >= 0)
        {
            var providerId = value[..slash];
            var upstreamModel = value[(slash + 1)..];
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(upstreamModel))
                throw new RouteResolutionException($"Unknown model '{value}'.");

            var provider = FindProvider(providerId)
                ?? throw new RouteResolutionException($"Unknown provider '{providerId}'.");

            return new ResolvedRoute(value, RoutingStrategy.Fallback, true,
                [new ResolvedTarget(provider.Definition.Id, upstreamModel)]);
        }

        var directProvider = FindProvider(value);
        if (directProvider is not null)
        {
            if (string.IsNullOrWhiteSpace(directProvider.Definition.DefaultModel))
                throw new RouteResolutionException($"Provider '{directProvider.Definition.Id}' has no default model.");

            return new ResolvedRoute(value, RoutingStrategy.Fallback, true,
                [new ResolvedTarget(directProvider.Definition.Id, directProvider.Definition.DefaultModel!)]);
        }

        var route = await _routes.GetAsync(value, ct);
        if (route is null || !route.Enabled)
            throw new RouteResolutionException($"Unknown model or route '{value}'.");

        var targets = route.Targets
            .Where(static target => target.Enabled)
            .Select(target => (Target: target, Provider: FindProvider(target.ProviderId)))
            .Where(static pair => pair.Provider is not null)
            .OrderBy(static pair => pair.Target.Priority)
            .ThenBy(static pair => pair.Provider!.Definition.Priority)
            .ThenBy(static pair => pair.Provider!.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Target.Model, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new ResolvedTarget(pair.Provider!.Definition.Id, pair.Target.Model))
            .ToArray();

        if (targets.Length == 0)
            throw new RouteResolutionException($"Route '{route.Id}' has no enabled targets.");

        return new ResolvedRoute(route.Id, route.Strategy, false, targets);
    }

    private async Task<ResolvedRoute> ResolveAllAsync(CancellationToken ct)
    {
        var targets = new List<(ProviderDefinition Provider, string Model)>();
        foreach (var provider in _providers.Snapshot)
        {
            ct.ThrowIfCancellationRequested();
            var definition = provider.Definition;
            IReadOnlyList<string> models = definition.Models ?? [];
            if (models.Count == 0 && definition.DiscoverModels)
                models = await _providers.ListModelsAsync(definition.Id, ct);
            if (models.Count == 0 && !string.IsNullOrWhiteSpace(definition.DefaultModel))
                models = [definition.DefaultModel!];

            foreach (var upstreamModel in models.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
                targets.Add((definition, upstreamModel));
        }

        var resolved = targets
            .OrderBy(static item => item.Provider.Priority)
            .ThenBy(static item => item.Provider.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Model, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new ResolvedTarget(item.Provider.Id, item.Model))
            .ToArray();

        if (resolved.Length == 0)
            throw new RouteResolutionException("No enabled provider models are available.");

        return new ResolvedRoute("all", RoutingStrategy.Fallback, false, resolved);
    }

    private IAiProvider? FindProvider(string id) =>
        _providers.Snapshot.FirstOrDefault(provider =>
            string.Equals(provider.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
}
