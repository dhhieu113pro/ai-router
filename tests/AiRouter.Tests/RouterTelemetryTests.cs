using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryTests
{
    [Fact]
    public void Collector_is_bounded_and_returns_newest_first()
    {
        var telemetry = new InMemoryRouterTelemetry(2);
        telemetry.Record(Record("p1", 1));
        telemetry.Record(Record("p2", 2));
        telemetry.Record(Record("p3", 3));

        var recent = telemetry.Recent();
        Assert.Equal(2, recent.Count);
        Assert.Equal("p3", recent[0].ProviderId);
        Assert.Equal("p2", recent[1].ProviderId);
    }

    [Fact]
    public void Summary_reports_cache_ratio_coverage_and_cost_truthfully()
    {
        var telemetry = new InMemoryRouterTelemetry();
        telemetry.Record(Record("p1", 1, new ProviderUsage(100, 10, 110, 80, null, null), 0.01m));
        telemetry.Record(Record("p1", 2, new ProviderUsage(50, 5, 55, null, null, null), null));
        telemetry.Record(Record("p2", 3, null, null, success: false));

        var summary = telemetry.Summary();

        Assert.Equal(3, summary.RequestCount);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(1, summary.CacheCoverageCount);
        Assert.Equal(0.8m, summary.CacheRatio);
        Assert.Equal(0.01m, summary.TotalCost);
        Assert.Equal(2, summary.Providers.Count);
    }

    private static RouterTelemetryRecord Record(string provider, int attempt, ProviderUsage? usage = null, decimal? cost = null, bool success = true) =>
        new(DateTimeOffset.UtcNow, "route", provider, "model", RoutingStrategy.Sticky, false, true, attempt > 1, "hit", attempt,
            TimeSpan.FromMilliseconds(10 * attempt), usage, cost, cost is null ? null : "reported", success, success ? 200 : 503,
            success ? ProviderFailureKind.None : ProviderFailureKind.ProviderFailure);
}
