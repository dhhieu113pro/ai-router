using AiRouter.Providers;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class RouterCostCoverageTests
{
    [Fact]
    public void Resolve_marks_computed_cost_as_estimated()
    {
        var usage = new ProviderUsage(1000, 100, 1100, null, null, null);
        var provider = new ProviderDefinition("p", "p", "fake", "https://example.test", null,
            InputPricePerMillion: 1m, OutputPricePerMillion: 2m);

        var result = CostEstimator.Resolve(usage, provider)!;

        Assert.Equal("estimated", result.Source);
        Assert.Equal(0.0012m, result.Value);
    }
}
