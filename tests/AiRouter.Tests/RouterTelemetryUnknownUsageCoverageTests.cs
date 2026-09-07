using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryUnknownUsageCoverageTests
{
    [Fact]
    public void Unknown_cache_usage_keeps_ratio_null_and_coverage_zero()
    {
        var telemetry = new InMemoryRouterTelemetry();
        telemetry.Record(new RouterTelemetryRecord(
            DateTimeOffset.UtcNow,
            "route",
            "provider",
            "model",
            RoutingStrategy.Fallback,
            false,
            false,
            false,
            "route",
            1,
            TimeSpan.FromMilliseconds(2),
            new ProviderUsage(10, 2, 12, null, null, null),
            null,
            null,
            true,
            200,
            ProviderFailureKind.None));

        var summary = telemetry.Summary();

        Assert.Null(summary.CacheRatio);
        Assert.Equal(0, summary.CacheCoverageCount);
        Assert.Equal(0m, summary.CacheCoveragePercentage);
    }
}
