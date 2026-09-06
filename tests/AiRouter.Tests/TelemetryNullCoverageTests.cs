using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class TelemetryNullCoverageTests
{
    [Fact]
    public void Record_rejects_null()
    {
        var telemetry = new InMemoryRouterTelemetry();
        Assert.Throws<ArgumentNullException>(() => telemetry.Record(null!));
    }
}
