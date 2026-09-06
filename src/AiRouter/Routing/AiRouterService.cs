using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;

namespace AiRouter.Routing;

public sealed class AiRouterService : IAiRouter
{
    private readonly RouteResolver _resolver;
    private readonly IProviderManager _providers;
    private readonly AiRouterOptions _options;
    private readonly IAffinityStore _affinity;
    private readonly ConcurrentDictionary<string, int> _roundRobinIndices = new(StringComparer.OrdinalIgnoreCase);

    public AiRouterService(RouteResolver resolver, IProviderManager providers, AiRouterOptions? options = null, IAffinityStore? affinity = null)
    {
        _resolver = resolver;
        _providers = providers;
        _options = options ?? new AiRouterOptions();
        _affinity = affinity ?? new InMemoryAffinityStore();
    }

    public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
        ChatAsync(model, body, null, stream, ct);

    public Task<RouterResult> ChatAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) =>
        ExecuteAsync(model, body, requestContext, stream, static (provider, targetModel, request, isStream, token) =>
            provider.SendChatAsync(targetModel, request, isStream, token), ct);

    public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
        ResponsesAsync(model, body, null, stream, ct);

    public Task<RouterResult> ResponsesAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) =>
        ExecuteAsync(model, body, requestContext, stream, static (provider, targetModel, request, isStream, token) =>
            provider.SendResponsesAsync(targetModel, request, isStream, token), ct);

    private async Task<RouterResult> ExecuteAsync(
        string model,
        JsonElement body,
        RouterRequestContext? requestContext,
        bool stream,
        Func<IAiProvider, string, JsonElement, bool, CancellationToken, Task<ProviderResponse>> send,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        ResolvedRoute route;
        try { route = await _resolver.ResolveAsync(model, ct); }
        catch (RouteResolutionException ex)
        {
            return new RouterResult { Success = false, StatusCode = 400, FailureKind = ProviderFailureKind.InvalidRequest, ErrorMessage = ex.Message };
        }

        var resolved = route.Targets
            .Select(target => (Target: target, Provider: FindProvider(target.ProviderId)))
            .Where(static item => item.Provider is not null)
            .Select(static item => (item.Target, Provider: item.Provider!))
            .ToArray();
        if (resolved.Length == 0) return Unavailable("No enabled providers are available for this route.");

        var eligible = resolved.Where(item => !IsCoolingDown(item.Provider)).ToArray();
        if (eligible.Length == 0) eligible = resolved;

        var affinitySource = requestContext?.AffinitySource ?? "route";
        var classification = route.Pinned ? "pinned" : "route";
        var affinityApplied = false;
        AffinityEntry? priorAffinity = null;

        if (route.Strategy == RoutingStrategy.RoundRobin && !route.Pinned && eligible.Length > 1)
        {
            var start = _roundRobinIndices.AddOrUpdate(route.RouteId, 0, static (_, current) => unchecked(current + 1));
            eligible = Rotate(eligible, (int)((uint)start % (uint)eligible.Length));
        }
        else if (route.Strategy == RoutingStrategy.Sticky && !route.Pinned && eligible.Length > 1)
        {
            var key = requestContext?.AffinityKey;
            if (!string.IsNullOrWhiteSpace(key) && _affinity.TryGet(route.RouteId, key, DateTimeOffset.UtcNow, out var stored))
            {
                var index = Array.FindIndex(eligible, x => SameTarget(x.Target, stored));
                if (index >= 0)
                {
                    eligible = Rotate(eligible, index);
                    priorAffinity = stored;
                    affinityApplied = true;
                    classification = "hit";
                }
                else
                {
                    classification = "miss";
                }
            }
            else
            {
                classification = string.IsNullOrWhiteSpace(key) ? "route" : "miss";
            }

            if (!affinityApplied)
            {
                var material = route.RouteId + "\n" + (requestContext?.AffinityKey ?? string.Empty);
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
                eligible = Rotate(eligible, (int)(BitConverter.ToUInt32(hash, 0) % (uint)eligible.Length));
            }
        }

        ProviderResponse? lastResponse = null;
        ResolvedTarget? lastTarget = null;
        var attempts = 0;
        foreach (var item in eligible)
        {
            attempts++;
            ct.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            MarkRequest(item.Provider);

            ProviderResponse response;
            try { response = await send(item.Provider, item.Target.Model, body, stream, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException ex) { response = ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 504, ex.Message); }
            catch (Exception ex) { response = ProviderResponse.Failed(ProviderFailureKind.ProviderFailure, 503, ex.Message); }

            var latency = Stopwatch.GetElapsedTime(started);
            if (response.Success)
            {
                MarkSuccess(item.Provider, latency);
                var rebound = false;
                if (route.Strategy == RoutingStrategy.Sticky && !route.Pinned && requestContext?.AffinityKey is { Length: > 0 } key)
                {
                    rebound = attempts > 1 || (priorAffinity is not null && !SameTarget(item.Target, priorAffinity));
                    _affinity.Set(route.RouteId, key, item.Target, DateTimeOffset.UtcNow, _options.StickyAffinityTtl);
                }
                return Map(response, item.Target, route, affinityApplied, affinitySource, rebound, attempts > 1, attempts, classification);
            }

            MarkFailure(item.Provider, response, latency);
            lastResponse = response;
            lastTarget = item.Target;
            if (response.StreamCommitted || response.FailureKind is ProviderFailureKind.InvalidRequest or ProviderFailureKind.Cancelled)
                return Map(response, item.Target, route, affinityApplied, affinitySource, false, attempts > 1, attempts, classification);
            if (route.Pinned) break;
        }

        return Map(lastResponse!, lastTarget!, route, affinityApplied, affinitySource, false, attempts > 1, attempts, classification);
    }

    private static (ResolvedTarget Target, IAiProvider Provider)[] Rotate((ResolvedTarget Target, IAiProvider Provider)[] items, int start) =>
        Enumerable.Range(0, items.Length).Select(index => items[(start + index) % items.Length]).ToArray();

    private static bool SameTarget(ResolvedTarget target, AffinityEntry entry) =>
        string.Equals(target.ProviderId, entry.ProviderId, StringComparison.OrdinalIgnoreCase) && string.Equals(target.Model, entry.Model, StringComparison.Ordinal);

    private IAiProvider? FindProvider(string id) => _providers.Snapshot.FirstOrDefault(provider => string.Equals(provider.Definition.Id, id, StringComparison.OrdinalIgnoreCase));

    private bool IsCoolingDown(IAiProvider provider)
    {
        var health = provider.Health;
        lock (health)
        {
            if (health.CooldownUntil is not { } until) return false;
            if (until > DateTimeOffset.UtcNow) return true;
            health.CooldownUntil = null;
            if (health.Status == ProviderStatus.CoolingDown) health.Status = ProviderStatus.Degraded;
            return false;
        }
    }

    private static void MarkRequest(IAiProvider provider) { lock (provider.Health) provider.Health.LastRequestAt = DateTimeOffset.UtcNow; }
    private static void MarkSuccess(IAiProvider provider, TimeSpan latency)
    {
        lock (provider.Health)
        {
            var now = DateTimeOffset.UtcNow;
            provider.Health.Status = ProviderStatus.Healthy;
            provider.Health.ConsecutiveFailures = 0;
            provider.Health.CooldownUntil = null;
            provider.Health.LastSuccessAt = now;
            provider.Health.LastError = null;
            provider.Health.LastLatency = latency;
        }
    }

    private void MarkFailure(IAiProvider provider, ProviderResponse response, TimeSpan latency)
    {
        lock (provider.Health)
        {
            var health = provider.Health;
            var now = DateTimeOffset.UtcNow;
            health.LastFailureAt = now;
            health.LastError = response.ErrorMessage;
            health.LastLatency = latency;
            switch (response.FailureKind)
            {
                case ProviderFailureKind.RateLimited:
                    health.ConsecutiveFailures++;
                    health.Status = ProviderStatus.CoolingDown;
                    health.CooldownUntil = response.RetryAfter is { } retryAfter && retryAfter > now ? retryAfter : now + _options.RateLimitCooldown;
                    break;
                case ProviderFailureKind.ProviderFailure:
                    health.ConsecutiveFailures++;
                    if (health.ConsecutiveFailures >= Math.Max(1, _options.ConsecutiveFailuresBeforeCooldown))
                    { health.Status = ProviderStatus.CoolingDown; health.CooldownUntil = now + _options.ErrorCooldown; }
                    else health.Status = ProviderStatus.Degraded;
                    break;
                case ProviderFailureKind.TargetFailure: health.Status = ProviderStatus.Degraded; break;
            }
        }
    }

    private static RouterResult Map(ProviderResponse response, ResolvedTarget target, ResolvedRoute route, bool applied, string source, bool rebound, bool fallback, int attempts, string classification) => new()
    {
        Success = response.Success, StatusCode = response.StatusCode, ProviderId = target.ProviderId, Model = target.Model,
        Body = response.Body, Stream = response.Stream, ContentType = response.ContentType, ErrorMessage = response.ErrorMessage, FailureKind = response.FailureKind,
        AffinityApplied = applied, AffinitySource = source, AffinityRebound = rebound, FallbackOccurred = fallback, AttemptCount = attempts,
        AffinityClassification = route.Pinned ? "pinned" : classification
    };

    private static RouterResult Unavailable(string message) => new() { Success = false, StatusCode = 503, FailureKind = ProviderFailureKind.ProviderFailure, ErrorMessage = message };
}
