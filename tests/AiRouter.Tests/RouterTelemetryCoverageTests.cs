using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterTelemetryCoverageTests
{
    [Fact]
    public void Capacity_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRouterTelemetry(0));
    }
}
