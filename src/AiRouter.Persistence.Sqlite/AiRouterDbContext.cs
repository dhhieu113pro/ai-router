using Microsoft.EntityFrameworkCore;

namespace AiRouter.Persistence.Sqlite;

internal sealed class AiRouterDbContext(DbContextOptions<AiRouterDbContext> options) : DbContext(options)
{
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();
    public DbSet<RouteEntity> Routes => Set<RouteEntity>();
    public DbSet<RouteTargetEntity> RouteTargets => Set<RouteTargetEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<RouteEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<RouteTargetEntity>().HasKey(x => new { x.RouteId, x.Ordinal });
        modelBuilder.Entity<RouteEntity>()
            .HasMany(x => x.Targets)
            .WithOne()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProviderEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public long? TimeoutMilliseconds { get; set; }
    public string ModelsJson { get; set; } = "[]";
    public string? DefaultModel { get; set; }
    public bool DiscoverModels { get; set; }
    public string ExtraHeadersJson { get; set; } = "{}";
    public string? ChatEndpoint { get; set; }
    public string? ResponsesEndpoint { get; set; }
    public string? ModelsEndpoint { get; set; }
    public bool SupportsNativeResponses { get; set; }
}

internal sealed class RouteEntity
{
    public string Id { get; set; } = string.Empty;
    public int Strategy { get; set; }
    public bool Enabled { get; set; }
    public List<RouteTargetEntity> Targets { get; set; } = [];
}

internal sealed class RouteTargetEntity
{
    public string RouteId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}
