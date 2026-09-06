# Cache-Affinity Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in sticky session routing, truthful cache/cost telemetry, an authenticated cache probe, and admin observability without changing existing Fallback, RoundRobin, or direct-pinning semantics.

**Architecture:** Keep affinity and telemetry in the `AiRouter` core behind small in-memory interfaces, pass transport-derived affinity through a provider-neutral `RouterRequestContext`, and let ASP.NET Core derive keys/emit headers and expose management endpoints. OpenAI-compatible providers normalize usage metadata; the Angular admin consumes only management DTOs and never sees prompts or raw session identifiers.

**Tech Stack:** .NET 10, ASP.NET Core, System.Text.Json, concurrent in-memory stores, xUnit, Angular standalone components/HttpClient.

**Spec:** `docs/superpowers/specs/2026-09-06-cache-affinity-routing-design.md`

## Global Constraints

- `RoutingStrategy.Sticky` is opt-in; existing route defaults remain unchanged.
- Default sticky affinity TTL is 30 minutes sliding.
- Default recent telemetry capacity is 1,000 records.
- Default cache-probe repeat count is 3 and maximum is 5.
- Never retain prompts, response bodies, API keys, raw session identifiers, or affinity hashes in telemetry.
- Cache metrics are reported only when upstream usage exposes them; never fabricate cache hits.
- Cost is reported by the provider when available, otherwise estimated only when all required token counts/prices are known.
- Existing `Fallback`, `RoundRobin`, direct `provider/model`, OpenAI endpoints, and existing response headers remain backward compatible.
- Tool batching, context compaction, and agent-loop behavior are out of scope.

---

### Task 1: Core affinity contracts and bounded store

**Files:**
- Modify: `src/AiRouter/Routing/RoutingStrategy.cs`
- Create: `src/AiRouter/Routing/RouterRequestContext.cs`
- Create: `src/AiRouter/Routing/IAffinityStore.cs`
- Create: `src/AiRouter/Routing/InMemoryAffinityStore.cs`
- Modify: `src/AiRouter/Configuration/AiRouterOptions.cs`
- Modify: `src/AiRouter/DependencyInjection.cs`
- Create: `tests/AiRouter.Tests/AffinityStoreTests.cs`

**Interfaces:**
- Produces: `RoutingStrategy.Sticky`.
- Produces: `RouterRequestContext(string? AffinityKey = null, string AffinitySource = "route", string? RequestId = null)`.
- Produces: `AffinityEntry(string ProviderId, string Model, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt)`.
- Produces: `IAffinityStore.TryGet(string routeId, string affinityKey, DateTimeOffset now, out AffinityEntry entry)` and `Set(string routeId, string affinityKey, ResolvedTarget target, DateTimeOffset now, TimeSpan ttl)`.
- Produces options `StickyAffinityTtl = TimeSpan.FromMinutes(30)`, `TelemetryRecentCapacity = 1000`, `CacheProbeMaxRepeats = 5`.

- [ ] **Step 1: Write failing affinity-store tests** covering first miss, set/get, sliding expiry refresh, expired-entry removal, and separation by route/key. Assert `RoutingStrategy.Sticky` exists and option defaults match the spec.

```csharp
[Fact]
public void Set_then_get_returns_target_and_slides_expiration()
{
    var store = new InMemoryAffinityStore();
    var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
    store.Set("coding", "abc", new ResolvedTarget("p1", "m1"), now, TimeSpan.FromMinutes(30));

    Assert.True(store.TryGet("coding", "abc", now.AddMinutes(10), out var entry));
    Assert.Equal("p1", entry.ProviderId);
    Assert.Equal(now.AddMinutes(40), entry.ExpiresAt);
}
```

- [ ] **Step 2: Run `dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj --filter AffinityStoreTests`** and verify failure because the new contracts do not exist.

- [ ] **Step 3: Implement the contracts and store** with a `ConcurrentDictionary<(string RouteId,string Key), AffinityEntry>`, lazy expiry, and sliding refresh on successful `TryGet`; register one singleton `IAffinityStore` in `AddAiRouter()`.

- [ ] **Step 4: Re-run the focused tests** and verify PASS, then run existing `RouteResolverTests` to confirm enum expansion does not alter existing resolution.

- [ ] **Step 5: Commit** with `feat: add sticky affinity primitives`.

---

### Task 2: Sticky routing, failover, and result metadata

**Files:**
- Modify: `src/AiRouter/Routing/IAiRouter.cs`
- Modify: `src/AiRouter/Routing/AiRouterService.cs`
- Modify: `src/AiRouter/Routing/RouteDefinition.cs`
- Modify: `tests/AiRouter.Tests/AiRouterServiceTests.cs`
- Create: `tests/AiRouter.Tests/StickyRoutingTests.cs`

**Interfaces:**
- Consumes: `IAffinityStore`, `RouterRequestContext`, `RoutingStrategy.Sticky`.
- Produces overloads `ChatAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default)` and equivalent `ResponsesAsync`; preserve existing overloads by forwarding with `requestContext: null`.
- Produces `RouterResult` metadata: `AffinityApplied`, `AffinitySource`, `AffinityRebound`, `FallbackOccurred`, `AttemptCount`, and an affinity classification suitable for `hit|miss|route|pinned`.

- [ ] **Step 1: Write failing sticky-routing tests** for same key/same target, deterministic distribution for different keys, route-only deterministic selection, hard pin bypass, cooling/deleted target rejection, rate-limit/provider-failure fallback + rebind, invalid-request no retry, and unchanged RoundRobin/Fallback behavior.

```csharp
[Fact]
public async Task Sticky_route_rebinds_after_rate_limit()
{
    var first = FakeProvider.RateLimited("p1");
    var second = FakeProvider.Success("p2");
    var router = CreateStickyRouter(first, second);
    var ctx = new RouterRequestContext("session-hash", "header");

    var result = await router.ChatAsync("coding", Request(), ctx);

    Assert.True(result.Success);
    Assert.Equal("p2", result.ProviderId);
    Assert.True(result.FallbackOccurred);
    Assert.True(result.AffinityRebound);
    Assert.Equal(2, result.AttemptCount);
}
```

- [ ] **Step 2: Run the focused tests** and verify they fail on missing overloads/behavior.

- [ ] **Step 3: Implement sticky ordering in `AiRouterService.ExecuteAsync`**: resolve eligible targets; for Sticky, move valid stored target first or rotate deterministically using SHA-256 of `routeId + affinityKey` (route id alone when unkeyed); reuse current stop/retry classification; write affinity only after success; mark rebind when success differs from a prior valid affinity or follows failover.

- [ ] **Step 4: Run `StickyRoutingTests` plus all existing `AiRouterServiceTests`, `AiRouterFailureKindCoverageTests`, and `RouteResolverTests`** and verify PASS.

- [ ] **Step 5: Commit** with `feat: add sticky session routing`.

---

### Task 3: HTTP affinity-key derivation and routing headers

**Files:**
- Create: `src/AiRouter.AspNetCore/AffinityKeyResolver.cs`
- Modify: `src/AiRouter.AspNetCore/EndpointRouteBuilderExtensions.cs`
- Create: `tests/AiRouter.AspNetCore.Tests/AffinityRoutingApiTests.cs`

**Interfaces:**
- Consumes: context-aware `IAiRouter` overloads and `RouterResult` metadata.
- Produces: `AffinityKeyResolver.Resolve(HttpContext context, string routeId, JsonElement body) -> RouterRequestContext`.
- Header precedence: `X-AiRouter-Session` > body `user` > stable system/developer prefix > route-only.
- Produces additive headers `X-AiRouter-Affinity`, `X-AiRouter-Affinity-Source`, `X-AiRouter-Fallback`, `X-AiRouter-Attempts`.

- [ ] **Step 1: Write failing API tests** that send conflicting header/user/prefix identities and assert precedence; test Chat Completions `messages` and Responses `instructions`/leading developer-system input; assert raw session text never appears in headers; assert pinned requests emit `pinned`.

- [ ] **Step 2: Run `dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj --filter AffinityRoutingApiTests`** and verify failure.

- [ ] **Step 3: Implement `AffinityKeyResolver`** using SHA-256 hex of the selected identity. Normalize only stable system/developer text; do not include mutable user/tool output. For no usable prefix, set `AffinityKey = null` and source `route`. Pass the context to core and emit metadata headers after routing.

- [ ] **Step 4: Run focused API tests plus `EmbeddedHostingTests` and `UseAiRouterTests`** and verify existing endpoint contracts remain green.

- [ ] **Step 5: Commit** with `feat: expose cache affinity over OpenAI endpoints`.

---

### Task 4: Provider-neutral usage normalization and pricing

**Files:**
- Create: `src/AiRouter/Providers/ProviderUsage.cs`
- Modify: `src/AiRouter/Providers/ProviderResponse.cs`
- Modify: `src/AiRouter/Providers/ProviderDefinition.cs`
- Modify: `src/AiRouter/Providers/OpenAI/OpenAiCompatibleProvider.cs`
- Create: `tests/AiRouter.Providers.OpenAI.Tests/OpenAiUsageTests.cs`
- Modify: `tests/AiRouter.Tests/ProviderContractTests.cs`

**Interfaces:**
- Produces: `ProviderUsage(int? InputTokens, int? OutputTokens, int? TotalTokens, int? CachedInputTokens, int? CacheWriteTokens, decimal? ReportedCost)`.
- Adds optional provider pricing: `InputPricePerMillion`, `CachedInputPricePerMillion`, `OutputPricePerMillion`.
- `ProviderResponse.Usage` carries normalized data without changing response body/stream semantics.

- [ ] **Step 1: Write failing normalization tests** for OpenAI `usage.prompt_tokens`, Responses-style `input_tokens`, `prompt_tokens_details.cached_tokens`/`input_tokens_details.cached_tokens`, absent details, and provider-reported cost when a supported field is present.

- [ ] **Step 2: Run provider tests** and verify failure on missing `ProviderUsage`.

- [ ] **Step 3: Implement a focused JSON usage parser in the OpenAI-compatible provider** after non-streaming response parsing. Keep nulls for unknown data and leave streaming usage null unless a terminal usage payload is already available without buffering/changing stream semantics.

- [ ] **Step 4: Add pricing properties to `ProviderDefinition` as nullable decimals**, verify old JSON/provider construction still compiles, and run OpenAI provider + provider contract tests.

- [ ] **Step 5: Commit** with `feat: normalize provider usage and pricing`.

---

### Task 5: Bounded telemetry and truthful cache/cost aggregates

**Files:**
- Create: `src/AiRouter/Telemetry/RouterTelemetryRecord.cs`
- Create: `src/AiRouter/Telemetry/IRouterTelemetry.cs`
- Create: `src/AiRouter/Telemetry/InMemoryRouterTelemetry.cs`
- Create: `src/AiRouter/Telemetry/CostEstimator.cs`
- Modify: `src/AiRouter/DependencyInjection.cs`
- Modify: `src/AiRouter/Routing/AiRouterService.cs`
- Create: `tests/AiRouter.Tests/RouterTelemetryTests.cs`
- Create: `tests/AiRouter.Tests/CostEstimatorTests.cs`

**Interfaces:**
- Produces `IRouterTelemetry.Record(RouterTelemetryRecord record)`, `Recent()`, and `Summary()`.
- `RouterTelemetryRecord` contains timestamp, route/provider/model, strategy, pinned/sticky/fallback/affinity classification, attempts, latency, usage, cost value/source, status/failure kind; it has no request body or affinity key field.
- Cache ratio is `sum(cachedInputTokens) / sum(inputTokens)` only across records where both values are known; summary also exposes coverage count/percentage.

- [ ] **Step 1: Write failing tests** for capacity 1,000 eviction, aggregate success/error counts, cache ratio and coverage, reported-cost precedence, estimated cost with complete prices, and null estimate when cached tokens exist but cached-input pricing is missing.

```csharp
[Fact]
public void Cost_is_null_when_cached_price_is_required_but_missing()
{
    var usage = new ProviderUsage(1000, 100, 1100, 800, null, null);
    var pricing = new ProviderDefinition(/* existing required args */, InputPricePerMillion: 1m, OutputPricePerMillion: 2m);
    Assert.Null(CostEstimator.Estimate(usage, pricing));
}
```

- [ ] **Step 2: Run focused tests** and verify failure.

- [ ] **Step 3: Implement the bounded collector and estimator**. Make `Record` non-throwing at the router boundary (`try/catch` around telemetry only) so telemetry failure never changes request success.

- [ ] **Step 4: Wire one record per completed router request** including failed requests, selected target/last target, total elapsed latency, attempts, normalized usage and cost source (`reported|estimated|null`). Run all core tests.

- [ ] **Step 5: Commit** with `feat: add cache and cost telemetry`.

---

### Task 6: Authenticated telemetry management API

**Files:**
- Modify: `src/AiRouter.AspNetCore/ManagementEndpointRouteBuilderExtensions.cs`
- Create: `tests/AiRouter.AspNetCore.Tests/TelemetryManagementApiTests.cs`

**Interfaces:**
- Consumes: `IRouterTelemetry.Summary()` and `Recent()`.
- Produces authenticated `GET /telemetry/summary` and `GET /telemetry/recent` under the existing management endpoint prefix.

- [ ] **Step 1: Write failing tests** for 401 without admin bearer key, 200 with key, summary shape including cache coverage/cost, recent bounded records, and absence of prompt/session/affinity-key fields.

- [ ] **Step 2: Run focused management tests** and verify failure because endpoints are unmapped.

- [ ] **Step 3: Map both endpoints using existing `BearerKeyAuthorizer` and `JsonAsync` conventions**; do not introduce a second auth mechanism.

- [ ] **Step 4: Run `TelemetryManagementApiTests`, `ManagementApiTests`, and `ManagementBranchCoverageTests`** and verify PASS.

- [ ] **Step 5: Commit** with `feat: expose router telemetry management API`.

---

### Task 7: Controlled cache-affinity probe

**Files:**
- Create: `src/AiRouter.AspNetCore/CacheProbe.cs`
- Modify: `src/AiRouter.AspNetCore/ManagementEndpointRouteBuilderExtensions.cs`
- Create: `tests/AiRouter.AspNetCore.Tests/CacheProbeApiTests.cs`

**Interfaces:**
- Produces authenticated `POST /probe/cache`.
- Request DTO: `CacheProbeRequest(string Model, JsonElement Request, int Repeats = 3)`.
- Response contains per-attempt provider/model/latency/usage/cost/affinity plus diagnostics; probe uses one generated opaque session identity across repeats.

- [ ] **Step 1: Write failing tests** for default 3 repeats, max 5 validation, cancellation, stable target diagnostic, changed-target diagnostic, unavailable-cache-data diagnostic, zero-cache-ratio diagnostic only when cached-token fields are known, and recommendation for Sticky/direct pinning on instability.

- [ ] **Step 2: Run focused tests** and verify failure.

- [ ] **Step 3: Implement `CacheProbe`** by cloning the supplied OpenAI-compatible request, invoking normal `IAiRouter` routing with one generated hashed probe affinity context for every repetition, and collecting router result/usage metadata. Do not mutate route configuration.

- [ ] **Step 4: Map the endpoint behind existing bearer authorization**, reject repeats `<1` or `> options.CacheProbeMaxRepeats` with 400, and run focused + management regression tests.

- [ ] **Step 5: Commit** with `feat: add cache affinity probe`.

---

### Task 8: Admin Cache & Cost UI

**Files:**
- Modify: `src/AiRouter.Admin/src/app/models.ts`
- Modify: `src/AiRouter.Admin/src/app/api.service.ts`
- Modify: `src/AiRouter.Admin/src/app/api.service.spec.ts`
- Modify: `src/AiRouter.Admin/src/app/app.component.ts`
- Modify: `src/AiRouter.Admin/src/app/app.component.html`
- Modify: `src/AiRouter.Admin/src/app/app.component.css`
- Modify: `src/AiRouter.Admin/src/app/app.component.spec.ts`

**Interfaces:**
- Consumes: `/telemetry/summary`, `/telemetry/recent`, `/probe/cache`.
- Extends `Tab` with `observability`.
- Produces cards for cache ratio/coverage, tokens, cost, average latency; provider/route aggregates; recent requests; and probe form/results.

- [ ] **Step 1: Add failing `ApiService` tests** asserting GET summary/recent and POST probe URLs/payloads.

- [ ] **Step 2: Add failing component tests** asserting the Observability tab renders cache/cost cards and a probe result without exposing session identity.

- [ ] **Step 3: Implement typed Angular models and API methods**, then add `observability` to the existing tab union and refresh telemetry only when unlocked/selected.

- [ ] **Step 4: Implement the compact UI using existing design tokens/classes**: four metric cards, aggregate table, recent table, probe model/repeat inputs and run button. No chart library and no unrelated layout redesign.

- [ ] **Step 5: Run `npm test -- --watch=false` and `npm run build` from `src/AiRouter.Admin`** and verify PASS.

- [ ] **Step 6: Commit** with `feat: add cache and cost observability UI`.

---

### Task 9: Documentation, integration regression, and full verification

**Files:**
- Modify: `README.md`
- Modify: `docs/library-usage.md`
- Create: `tests/AiRouter.AspNetCore.Tests/StickyRoutingIntegrationTests.cs`

**Interfaces:**
- Documents stable `X-AiRouter-Session` use for coding-agent sessions, `Sticky` route configuration, new response headers, telemetry/probe endpoints, pricing fields, and the fact that provider pinning may still be required when a gateway hides multiple backend workers behind one provider identity.

- [ ] **Step 1: Write the integration test first** with two fake upstream providers: repeated same-session requests remain on provider A; induce A rate-limit/provider failure; next request falls back to B; following request stays on B.

- [ ] **Step 2: Run the integration test** and fix only feature defects revealed by it; do not broaden scope.

- [ ] **Step 3: Update README and library docs** with a concrete route example:

```json
{
  "id": "coding",
  "strategy": 2,
  "targets": [
    { "providerId": "openrouter-a", "model": "deepseek-v4-flash", "priority": 10 },
    { "providerId": "openrouter-b", "model": "deepseek-v4-flash", "priority": 20 }
  ]
}
```

and client guidance to send `X-AiRouter-Session: <stable-conversation-id>`; explain that AI Router hashes it and never emits/stores the raw value in telemetry.

- [ ] **Step 4: Run full .NET verification**: `dotnet test AiRouter.slnx` and the repository's branch-coverage verification command used by CI. Expected: all tests pass and existing coverage gate remains satisfied.

- [ ] **Step 5: Run full Angular verification**: from `src/AiRouter.Admin`, `npm ci`, `npm test -- --watch=false`, `npm run build`. Expected: PASS.

- [ ] **Step 6: Review the branch diff against the spec** and confirm no agent batching/compaction code, raw session storage, prompt telemetry, default strategy change, or unrelated refactor was introduced.

- [ ] **Step 7: Commit** with `docs: document cache affinity routing`.

- [ ] **Step 8: Push/update PR #6 and mark ready only after CI is green.**
