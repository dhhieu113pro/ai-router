using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class AffinityEntryCoverageTests
{
    [Fact]
    public void Affinity_entry_exposes_all_timestamps()
    {
        var created = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        var lastUsed = created.AddMinutes(1);
        var expires = created.AddMinutes(30);

        var entry = new AffinityEntry("provider", "model", created, lastUsed, expires);

        Assert.Equal(created, entry.CreatedAt);
        Assert.Equal(lastUsed, entry.LastUsedAt);
        Assert.Equal(expires, entry.ExpiresAt);
    }
}
