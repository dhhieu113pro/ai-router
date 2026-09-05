using Microsoft.EntityFrameworkCore;

namespace AiRouter.Persistence.Sqlite;

internal static class SqliteContextFactory
{
    public static AiRouterDbContext Create(SqliteStoreOptions options)
    {
        var builder = new DbContextOptionsBuilder<AiRouterDbContext>();
        builder.UseSqlite(options.ConnectionString);
        return new AiRouterDbContext(builder.Options);
    }

    public static async Task<AiRouterDbContext> CreateInitializedAsync(SqliteStoreOptions options, CancellationToken ct)
    {
        var db = Create(options);
        try
        {
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
            return db;
        }
        catch
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
