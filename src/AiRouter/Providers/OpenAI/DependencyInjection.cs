using AiRouter.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiRouter.Providers.OpenAI;

public static class DependencyInjection
{
    public static IServiceCollection AddOpenAiCompatibleProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient();
        services.TryAddSingleton<IAiProviderFactory, OpenAiCompatibleProviderFactory>();
        return services;
    }
}
