using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.Persistence.Sqlite;

public static class DependencyInjection
{
    public static IServiceCollection AddAiRouterSqlite(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new SqliteStoreOptions(connectionString);
        services.AddSingleton(options);
        services.AddSingleton<IProviderStore, SqliteProviderStore>();
        services.AddSingleton<IRouteStore, SqliteRouteStore>();
        return services;
    }
}
