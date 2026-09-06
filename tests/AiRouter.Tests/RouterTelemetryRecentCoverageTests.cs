using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryRecentCoverageTests
{
    [Fact]
    public void Recent_is_newest_first_and_trims_to_capacity()
    {
        var telemetry = new InMemoryRouterTelemetry(1);
        telemetry.Record(Record("first"));
        telemetry.Record(Record("second"));

        var recent = telemetry.Recent();

        Assert.Single(recent);
        Assert.Equal("second", recent[0].RouteId);
    }

    private static RouterTelemetryRecord Record(string route) => new(
        DateTimeOffset.UtcNow,
        route,
        "provider",
        "model",
        RoutingStrategy.Fallback,
        false,
        false,
        false,
        "route",
        1,
        TimeSpan.Zero,
        null,
        null,
        null,
        true,
        200,
        ProviderFailureKind.None);
}
