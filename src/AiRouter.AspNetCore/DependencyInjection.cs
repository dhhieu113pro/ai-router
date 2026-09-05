namespace Microsoft.Extensions.DependencyInjection;

public static class AiRouterAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddAiRouterAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
