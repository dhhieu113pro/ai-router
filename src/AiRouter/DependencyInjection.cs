using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class AiRouterServiceCollectionExtensions
{
    public static IServiceCollection AddAiRouter(
        this IServiceCollection services,
        Action<AiRouterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AiRouterOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IProviderStore, InMemoryProviderStore>();
        services.TryAddSingleton<IRouteStore, InMemoryRouteStore>();
        services.TryAddSingleton<IProviderManager, ProviderManager>();
        services.TryAddSingleton<RouteResolver>();
        services.TryAddSingleton<IAiRouter, AiRouterService>();
        services.AddOpenAiCompatibleProvider();

        return services;
    }
}
