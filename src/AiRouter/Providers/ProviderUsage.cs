namespace AiRouter.Providers;

public sealed record ProviderUsage(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    int? CachedInputTokens,
    int? CacheWriteTokens,
    decimal? ReportedCost);
