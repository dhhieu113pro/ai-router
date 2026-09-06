namespace AiRouter.Telemetry;

public sealed class InMemoryRouterTelemetry : IRouterTelemetry
{
    private readonly int _capacity;
    private readonly Queue<RouterTelemetryRecord> _recent = new();
    private readonly object _gate = new();

    public InMemoryRouterTelemetry(int capacity = 1000)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Record(RouterTelemetryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _recent.Enqueue(record);
            while (_recent.Count > _capacity) _recent.Dequeue();
        }
    }

    public IReadOnlyList<RouterTelemetryRecord> Recent()
    {
        lock (_gate) return _recent.Reverse().ToArray();
    }

    public RouterTelemetrySummary Summary()
    {
        RouterTelemetryRecord[] snapshot;
        lock (_gate) snapshot = _recent.ToArray();
        return BuildSummary(snapshot);
    }

    private static RouterTelemetrySummary BuildSummary(RouterTelemetryRecord[] records)
    {
        var overall = Aggregate(records, "all");
        var providers = records.Where(r => !string.IsNullOrWhiteSpace(r.ProviderId))
            .GroupBy(r => r.ProviderId!, StringComparer.OrdinalIgnoreCase)
            .Select(g => Aggregate(g.ToArray(), g.Key))
            .OrderByDescending(g => g.RequestCount)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var routes = records.GroupBy(r => r.RouteId, StringComparer.OrdinalIgnoreCase)
            .Select(g => Aggregate(g.ToArray(), g.Key))
            .OrderByDescending(g => g.RequestCount)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RouterTelemetrySummary(
            overall.RequestCount,
            overall.SuccessCount,
            overall.ErrorCount,
            overall.AverageLatencyMs,
            overall.InputTokens,
            overall.OutputTokens,
            overall.CachedInputTokens,
            overall.CacheRatio,
            overall.CacheCoverageCount,
            overall.CacheCoveragePercentage,
            overall.TotalCost,
            providers,
            routes);
    }

    private static RouterTelemetryGroup Aggregate(RouterTelemetryRecord[] records, string key)
    {
        var requestCount = records.Length;
        var successCount = records.Count(r => r.Success);
        var input = records.Sum(r => (long)(r.Usage?.InputTokens ?? 0));
        var output = records.Sum(r => (long)(r.Usage?.OutputTokens ?? 0));
        var known = records.Where(r => r.Usage?.InputTokens is not null && r.Usage.CachedInputTokens is not null).ToArray();
        var knownInput = known.Sum(r => (long)r.Usage!.InputTokens!.Value);
        var cached = known.Sum(r => (long)r.Usage!.CachedInputTokens!.Value);
        decimal? ratio = known.Length == 0 || knownInput == 0 ? null : (decimal)cached / knownInput;
        var coverage = requestCount == 0 ? 0m : (decimal)known.Length / requestCount * 100m;
        var averageLatency = requestCount == 0 ? 0d : records.Average(r => r.Latency.TotalMilliseconds);
        var totalCost = records.Sum(r => r.Cost ?? 0m);
        return new RouterTelemetryGroup(
            key,
            requestCount,
            successCount,
            requestCount - successCount,
            averageLatency,
            input,
            output,
            cached,
            ratio,
            known.Length,
            coverage,
            totalCost);
    }
}
