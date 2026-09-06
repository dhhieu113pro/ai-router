using AiRouter.Configuration;
using AiRouter.Routing;

namespace AiRouter.Tests;

public sealed class AffinityStoreTests
{
    [Fact]
    public void First_lookup_is_a_miss()
    {
        var store = new InMemoryAffinityStore();
        Assert.False(store.TryGet("coding", "abc", DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void Set_then_get_returns_target_and_slides_expiration()
    {
        var store = new InMemoryAffinityStore();
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        store.Set("coding", "abc", new ResolvedTarget("p1", "m1"), now, TimeSpan.FromMinutes(30));

        Assert.True(store.TryGet("coding", "abc", now.AddMinutes(10), out var entry));
        Assert.Equal("p1", entry.ProviderId);
        Assert.Equal("m1", entry.Model);
        Assert.Equal(now.AddMinutes(10), entry.LastUsedAt);
        Assert.Equal(now.AddMinutes(40), entry.ExpiresAt);
    }

    [Fact]
    public void Expired_entry_is_removed()
    {
        var store = new InMemoryAffinityStore();
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        store.Set("coding", "abc", new ResolvedTarget("p1", "m1"), now, TimeSpan.FromMinutes(30));

        Assert.False(store.TryGet("coding", "abc", now.AddMinutes(31), out _));
        Assert.False(store.TryGet("coding", "abc", now.AddMinutes(32), out _));
    }

    [Fact]
    public void Entries_are_separated_by_route_and_key()
    {
        var store = new InMemoryAffinityStore();
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        store.Set("coding", "abc", new ResolvedTarget("p1", "m1"), now, TimeSpan.FromMinutes(30));
        store.Set("research", "abc", new ResolvedTarget("p2", "m2"), now, TimeSpan.FromMinutes(30));
        store.Set("coding", "xyz", new ResolvedTarget("p3", "m3"), now, TimeSpan.FromMinutes(30));

        Assert.True(store.TryGet("coding", "abc", now, out var first));
        Assert.True(store.TryGet("research", "abc", now, out var second));
        Assert.True(store.TryGet("coding", "xyz", now, out var third));
        Assert.Equal("p1", first.ProviderId);
        Assert.Equal("p2", second.ProviderId);
        Assert.Equal("p3", third.ProviderId);
    }

    [Fact]
    public void Non_positive_ttl_is_rejected()
    {
        var store = new InMemoryAffinityStore();
        var target = new ResolvedTarget("p1", "m1");
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Set("coding", "abc", target, now, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Set("coding", "abc", target, now, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Sticky_strategy_and_option_defaults_match_spec()
    {
        Assert.True(Enum.IsDefined(RoutingStrategy.Sticky));
        var options = new AiRouterOptions();
        Assert.Equal(TimeSpan.FromMinutes(30), options.StickyAffinityTtl);
        Assert.Equal(1000, options.TelemetryRecentCapacity);
        Assert.Equal(5, options.CacheProbeMaxRepeats);
    }
}
