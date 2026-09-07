using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryZeroInputCoverageTests
{
    [Fact]
    public void Zero_known_input_keeps_cache_ratio_null_but_counts_coverage()
    {
        var telemetry = new InMemoryRouterTelemetry();
        telemetry.Record(new RouterTelemetryRecord(
            DateTimeOffset.UtcNow,
            "route",
            "provider",
            "model",
            RoutingStrategy.Sticky,
            false,
            true,
            false,
            "hit",
            1,
            TimeSpan.Zero,
            new ProviderUsage(0, 0, 0, 0, null, null),
            null,
            null,
            true,
            200,
            ProviderFailureKind.None));

        var summary = telemetry.Summary();

        Assert.Null(summary.CacheRatio);
        Assert.Equal(1, summary.CacheCoverageCount);
        Assert.Equal(100m, summary.CacheCoveragePercentage);
    }
}
