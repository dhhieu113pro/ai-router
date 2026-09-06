using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;

namespace AiRouter.AspNetCore;

public sealed record CacheProbeRequest(string Model, JsonElement Request, int Repeats = 3);

public sealed record CacheProbeAttempt(
    int Index,
    bool Success,
    int StatusCode,
    string? ProviderId,
    string? Model,
    double LatencyMs,
    ProviderUsage? Usage,
    decimal? Cost,
    string? CostSource,
    string Affinity,
    bool FallbackOccurred,
    int AttemptCount);

public sealed record CacheProbeResult(
    int Repeats,
    IReadOnlyList<CacheProbeAttempt> Attempts,
    bool TargetChanged,
    decimal? CacheRatio,
    IReadOnlyList<string> Diagnostics,
    string? Recommendation);

public static class CacheProbe
{
    public static async Task<CacheProbeResult> RunAsync(
        IAiRouter router,
        IProviderManager providers,
        CacheProbeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(request);

        var affinityKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")))).ToLowerInvariant();
        var context = new RouterRequestContext(affinityKey, "probe");
        var attempts = new List<CacheProbeAttempt>(request.Repeats);
        var isResponses = request.Request.ValueKind == JsonValueKind.Object &&
            !request.Request.TryGetProperty("messages", out _) &&
            (request.Request.TryGetProperty("input", out _) || request.Request.TryGetProperty("instructions", out _));

        for (var i = 0; i < request.Repeats; i++)
        {
            ct.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var result = isResponses
                ? await router.ResponsesAsync(request.Model, request.Request.Clone(), context, false, ct).ConfigureAwait(false)
                : await router.ChatAsync(request.Model, request.Request.Clone(), context, false, ct).ConfigureAwait(false);

            var definition = result.ProviderId is null
                ? null
                : providers.Snapshot.FirstOrDefault(p => string.Equals(p.Definition.Id, result.ProviderId, StringComparison.OrdinalIgnoreCase))?.Definition;
            var cost = CostEstimator.Resolve(result.Usage, definition);
            attempts.Add(new CacheProbeAttempt(
                i + 1,
                result.Success,
                result.StatusCode,
                result.ProviderId,
                result.Model,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                result.Usage,
                cost?.Value,
                cost?.Source,
                result.AffinityClassification,
                result.FallbackOccurred,
                result.AttemptCount));
        }

        var targetChanged = attempts
            .Select(a => $"{a.ProviderId}\n{a.Model}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();

        var known = attempts.Where(a => a.Usage?.InputTokens is not null && a.Usage.CachedInputTokens is not null).ToArray();
        var knownInput = known.Sum(a => (long)a.Usage!.InputTokens!.Value);
        var cached = known.Sum(a => (long)a.Usage!.CachedInputTokens!.Value);
        decimal? cacheRatio = known.Length == 0 || knownInput == 0 ? null : (decimal)cached / knownInput;

        var diagnostics = new List<string>();
        if (targetChanged) diagnostics.Add("target_changed");
        if (known.Length == 0) diagnostics.Add("cache_data_unavailable");
        else if (cacheRatio == 0m) diagnostics.Add("cache_ratio_zero");

        return new CacheProbeResult(
            request.Repeats,
            attempts,
            targetChanged,
            cacheRatio,
            diagnostics,
            targetChanged ? "Use Sticky routing or direct provider/model pinning to preserve upstream cache locality." : null);
    }
}
