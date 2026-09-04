using AiRouter.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.Providers.OpenAI;

public static class DependencyInjection
{
    public static IServiceCollection AddOpenAiCompatibleProvider(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IAiProviderFactory, OpenAiCompatibleProviderFactory>();
        return services;
    }
}
