using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryEmptySummaryCoverageTests
{
    [Fact]
    public void Empty_summary_has_zero_totals_and_no_groups()
    {
        var summary = new InMemoryRouterTelemetry().Summary();

        Assert.Equal(0, summary.RequestCount);
        Assert.Equal(0d, summary.AverageLatencyMs);
        Assert.Null(summary.CacheRatio);
        Assert.Empty(summary.Providers);
        Assert.Empty(summary.Routes);
    }
}
