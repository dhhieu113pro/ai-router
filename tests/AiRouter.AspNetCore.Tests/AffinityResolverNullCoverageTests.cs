using System.Text.Json;
using AiRouter.AspNetCore;

namespace AiRouter.AspNetCore.Tests;

public sealed class AffinityResolverNullCoverageTests
{
    [Fact]
    public void Resolve_rejects_null_http_context()
    {
        var body = JsonDocument.Parse("{}").RootElement.Clone();
        Assert.Throws<ArgumentNullException>(() => AffinityKeyResolver.Resolve(null!, "route", body));
    }
}
