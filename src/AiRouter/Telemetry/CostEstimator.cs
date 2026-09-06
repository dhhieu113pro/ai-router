using AiRouter.Providers;

namespace AiRouter.Telemetry;

public sealed record RouterCost(decimal Value, string Source);

public static class CostEstimator
{
    public static RouterCost? Resolve(ProviderUsage? usage, ProviderDefinition? provider)
    {
        if (usage?.ReportedCost is decimal reported) return new RouterCost(reported, "reported");
        var estimated = Estimate(usage, provider);
        return estimated is decimal value ? new RouterCost(value, "estimated") : null;
    }

    public static decimal? Estimate(ProviderUsage? usage, ProviderDefinition? provider)
    {
        if (usage is null || provider is null ||
            usage.InputTokens is not int input || usage.OutputTokens is not int output ||
            provider.InputPricePerMillion is not decimal inputPrice || provider.OutputPricePerMillion is not decimal outputPrice)
            return null;

        decimal inputCost;
        if (usage.CachedInputTokens is int cached && cached > 0)
        {
            if (provider.CachedInputPricePerMillion is not decimal cachedPrice) return null;
            var uncached = Math.Max(0, input - cached);
            inputCost = uncached / 1_000_000m * inputPrice + cached / 1_000_000m * cachedPrice;
        }
        else
        {
            inputCost = input / 1_000_000m * inputPrice;
        }

        return inputCost + output / 1_000_000m * outputPrice;
    }
}
