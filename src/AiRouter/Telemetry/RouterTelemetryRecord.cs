using AiRouter.Providers;
using AiRouter.Routing;

namespace AiRouter.Telemetry;

public sealed record RouterTelemetryRecord(
    DateTimeOffset Timestamp,
    string RouteId,
    string? ProviderId,
    string? Model,
    RoutingStrategy Strategy,
    bool Pinned,
    bool Sticky,
    bool FallbackOccurred,
    string AffinityClassification,
    int AttemptCount,
    TimeSpan Latency,
    ProviderUsage? Usage,
    decimal? Cost,
    string? CostSource,
    bool Success,
    int StatusCode,
    ProviderFailureKind FailureKind);

public sealed record RouterTelemetryGroup(
    string Key,
    int RequestCount,
    int SuccessCount,
    int ErrorCount,
    double AverageLatencyMs,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal? CacheRatio,
    int CacheCoverageCount,
    decimal CacheCoveragePercentage,
    decimal TotalCost);

public sealed record RouterTelemetrySummary(
    int RequestCount,
    int SuccessCount,
    int ErrorCount,
    double AverageLatencyMs,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal? CacheRatio,
    int CacheCoverageCount,
    decimal CacheCoveragePercentage,
    decimal TotalCost,
    IReadOnlyList<RouterTelemetryGroup> Providers,
    IReadOnlyList<RouterTelemetryGroup> Routes);
