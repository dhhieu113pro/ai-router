namespace AiRouter.Configuration;

public sealed class AiRouterOptions
{
    public int ConsecutiveFailuresBeforeCooldown { get; set; } = 1;
    public TimeSpan ErrorCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RateLimitCooldown { get; set; } = TimeSpan.FromSeconds(60);
}
