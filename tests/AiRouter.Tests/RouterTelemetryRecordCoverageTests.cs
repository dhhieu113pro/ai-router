using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryRecordCoverageTests
{
    [Fact]
    public void Telemetry_record_exposes_cost_source_and_failure_kind()
    {
        var record = new RouterTelemetryRecord(
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
            null,
            0.1m,
            "estimated",
            false,
            503,
            ProviderFailureKind.ProviderFailure);

        Assert.Equal("estimated", record.CostSource);
        Assert.Equal(ProviderFailureKind.ProviderFailure, record.FailureKind);
    }
}
