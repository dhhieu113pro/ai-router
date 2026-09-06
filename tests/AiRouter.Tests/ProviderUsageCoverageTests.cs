using AiRouter.Providers;

namespace AiRouter.Tests;

public sealed class ProviderUsageCoverageTests
{
    [Fact]
    public void Provider_usage_exposes_optional_cache_creation_tokens()
    {
        var usage = new ProviderUsage(10, 2, 12, 5, 3, null);

        Assert.Equal(3, usage.CacheWriteTokens);
    }
}
