using AiRouter.Providers;
using AiRouter.Telemetry;

namespace AiRouter.Tests;

public sealed class CostEstimatorTests
{
    [Fact]
    public void Reported_cost_wins_over_pricing_estimate()
    {
        var usage = new ProviderUsage(1000, 100, 1100, 800, null, 0.123m);
        var provider = new ProviderDefinition("p", "p", "fake", "https://example.test", null,
            InputPricePerMillion: 1m, CachedInputPricePerMillion: 0.1m, OutputPricePerMillion: 2m);

        var cost = CostEstimator.Resolve(usage, provider)!;

        Assert.Equal("reported", cost.Source);
        Assert.Equal(0.123m, cost.Value);
    }

    [Fact]
    public void Estimate_uses_cached_and_uncached_input_prices()
    {
        var usage = new ProviderUsage(1000, 100, 1100, 800, null, null);
        var provider = new ProviderDefinition("p", "p", "fake", "https://example.test", null,
            InputPricePerMillion: 1m, CachedInputPricePerMillion: 0.1m, OutputPricePerMillion: 2m);

        var cost = CostEstimator.Estimate(usage, provider);

        Assert.Equal(0.00048m, cost);
    }

    [Fact]
    public void Cost_is_null_when_cached_price_is_required_but_missing()
    {
        var usage = new ProviderUsage(1000, 100, 1100, 800, null, null);
        var provider = new ProviderDefinition("p", "p", "fake", "https://example.test", null,
            InputPricePerMillion: 1m, OutputPricePerMillion: 2m);

        Assert.Null(CostEstimator.Estimate(usage, provider));
    }
}
