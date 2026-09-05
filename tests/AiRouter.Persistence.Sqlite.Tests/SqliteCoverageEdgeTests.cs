using System.Reflection;
using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.Persistence.Sqlite.Tests;

public sealed class SqliteCoverageEdgeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Options_require_connection_string(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new SqliteStoreOptions(connectionString));
    }

    [Fact]
    public void Dependency_injection_registers_options_and_both_stores()
    {
        var services = new ServiceCollection();

        var returned = services.AddAiRouterSqlite("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();

        Assert.Same(services, returned);
        Assert.Equal("Data Source=:memory:", provider.GetRequiredService<SqliteStoreOptions>().ConnectionString);
        Assert.IsType<SqliteProviderStore>(provider.GetRequiredService<IProviderStore>());
        Assert.IsType<SqliteRouteStore>(provider.GetRequiredService<IRouteStore>());
    }

    [Fact]
    public void Dependency_injection_rejects_null_service_collection()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddAiRouterSqlite("Data Source=:memory:"));
    }

    [Fact]
    public async Task Context_initialization_disposes_and_rethrows_when_ensure_created_is_cancelled()
    {
        var factory = typeof(SqliteStoreOptions).Assembly.GetType("AiRouter.Persistence.Sqlite.SqliteContextFactory", throwOnError: true)!;
        var method = factory.GetMethod("CreateInitializedAsync", BindingFlags.Public | BindingFlags.Static)!;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = (Task)method.Invoke(null, [new SqliteStoreOptions("Data Source=:memory:"), cts.Token])!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }
}
