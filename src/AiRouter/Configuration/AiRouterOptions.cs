namespace AiRouter.Configuration;

public sealed class AiRouterOptions
{
    public int ConsecutiveFailuresBeforeCooldown { get; set; } = 1;
    public TimeSpan ErrorCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RateLimitCooldown { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan StickyAffinityTtl { get; set; } = TimeSpan.FromMinutes(30);
    public int TelemetryRecentCapacity { get; set; } = 1000;
    public int CacheProbeMaxRepeats { get; set; } = 5;
}
