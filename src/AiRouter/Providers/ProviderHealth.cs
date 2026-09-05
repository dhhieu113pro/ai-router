namespace AiRouter.Providers;

public enum ProviderStatus
{
    Healthy,
    Degraded,
    CoolingDown,
    Disabled
}

public sealed class ProviderHealth
{
    public ProviderStatus Status { get; internal set; } = ProviderStatus.Healthy;
    public int ConsecutiveFailures { get; internal set; }
    public DateTimeOffset? CooldownUntil { get; internal set; }
    public DateTimeOffset? LastRequestAt { get; internal set; }
    public DateTimeOffset? LastSuccessAt { get; internal set; }
    public DateTimeOffset? LastFailureAt { get; internal set; }
    public string? LastError { get; internal set; }
    public TimeSpan? LastLatency { get; internal set; }
}
