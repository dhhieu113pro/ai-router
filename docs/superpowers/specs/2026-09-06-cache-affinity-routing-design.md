# Cache-Affinity Routing Design

## Status
Approved design for Phase 1 of AI Router cache-efficiency improvements.

## Problem
AI Router currently supports `Fallback` and `RoundRobin`. Round-robin advances on every request, so requests from one long-lived coding-agent session can land on different upstream providers or backends. For providers whose prompt cache is backend-local, this destroys cache warmth even when the prompt prefix is stable.

Direct `provider/model` addressing already behaves as a hard pin, and provider health/cooldown/fallback behavior already exists. Phase 1 should extend those existing primitives rather than add agent orchestration to the gateway.

## Goals
- Add a sticky routing mode that keeps one logical session on one route target while that target remains usable.
- Preserve current `Fallback` and `RoundRobin` behavior for backward compatibility.
- Make routing/cache behavior observable through response metadata and management telemetry.
- Add an authenticated probe that can detect unstable target selection and low/absent upstream cache reuse.
- Record usage, cached-token, latency, and estimated-cost data when upstream responses expose enough information.
- Fail over safely when the sticky target becomes unavailable, then rebind the session to the successful fallback target.

## Non-goals
- Tool-call batching.
- Moving tool output outside model context.
- Conversation compaction, masking, summarization, or context-window policy.
- Agent-loop turn limits or planning behavior.
- Reimplementing provider-native caches inside AI Router.

Those belong in an agent/harness above AI Router.

## Routing strategy
Add `RoutingStrategy.Sticky`.

`Fallback` remains ordered failover. `RoundRobin` remains per-request rotation. `Sticky` selects a target deterministically for a session, stores that affinity for a bounded period, and reuses it on subsequent requests.

Direct model identifiers such as `provider/model` and direct provider identifiers remain hard-pinned and do not use the affinity store.

## Affinity key
Affinity is only meaningful for non-pinned routes using `Sticky`.

The ASP.NET Core transport derives one opaque affinity key using this precedence:

1. `X-AiRouter-Session` request header when present and non-empty.
2. OpenAI-compatible request field `user` when present and a non-empty string.
3. A deterministic fingerprint derived from the stable request prefix.

The fingerprint fallback must not hash the entire mutable request body. It should use stable request identity inputs available in both Chat Completions and Responses requests: route id plus normalized leading system/developer content where available. If no stable prefix can be derived, the request is treated as unkeyed and Sticky falls back to a deterministic route-level selection rather than generating a random session identity.

Raw session identifiers and raw prompt content must never be stored in telemetry. Affinity storage uses a SHA-256-derived opaque key.

## Affinity storage
Introduce an internal `IAffinityStore` abstraction so routing is not coupled to a specific cache implementation.

Phase 1 ships an in-memory implementation registered by default.

Key: `(routeId, affinityKey)`.
Value: `providerId`, `model`, `createdAt`, `lastUsedAt`, `expiresAt`.

Default TTL: 30 minutes sliding, configurable through `AiRouterOptions.StickyAffinityTtl`.

Expired entries are ignored and lazily removed. No persistence is required in Phase 1; process restart may lose affinity.

## Sticky selection algorithm
For a resolved Sticky route:

1. Build the ordered eligible target list using existing route priority and provider health/cooldown filtering.
2. If an affinity entry exists and its exact provider/model target is still present and not cooling down, move it to the front.
3. If there is no usable affinity entry, select a deterministic starting target by hashing `routeId + affinityKey` modulo eligible target count. For unkeyed requests, hash `routeId` only.
4. Attempt targets in that order using existing failure classification rules.
5. On success, write/update affinity to the successful provider/model target.
6. On `RateLimited` or `ProviderFailure`, continue failover when safe under existing rules. A successful fallback becomes the new affinity target.
7. On `InvalidRequest`, cancellation, or committed stream failure, preserve current stop behavior and do not silently retry elsewhere.

This means Sticky prefers cache locality but never disables the gateway's existing resilience behavior.

## Router request context
`IAiRouter` currently receives only model, JSON body, stream flag, and cancellation token. Affinity metadata is transport-derived, so add a small router request context object rather than passing HTTP primitives into the core library.

Proposed fields:
- `string? AffinityKey`
- `string AffinitySource` with values `header`, `user`, `prefix`, or `route`
- optional correlation/request id for telemetry

The public API should preserve existing call patterns with overloads/defaults so current NuGet consumers continue compiling where practical.

## Router result metadata
Extend `RouterResult` with routing metadata sufficient for HTTP headers and telemetry:
- selected provider/model
- `AffinityApplied`
- `AffinitySource`
- `AffinityRebound`
- `FallbackOccurred`
- attempt count

ASP.NET Core emits:
- `X-AiRouter-Provider`
- `X-AiRouter-Model`
- `X-AiRouter-Affinity: hit|miss|route|pinned`
- `X-AiRouter-Affinity-Source: header|user|prefix|route`
- `X-AiRouter-Fallback: true|false`
- `X-AiRouter-Attempts: <n>`

No raw affinity key is returned.

## Usage and cache telemetry
Introduce provider-neutral usage metadata extracted from successful non-streaming JSON responses when present.

Minimum normalized fields:
- input tokens
- output tokens
- total tokens
- cached input tokens when exposed
- cache-write/creation tokens when exposed
- provider-reported cost when exposed

Provider adapters may populate normalized usage from provider-specific response shapes. Unknown fields remain null; AI Router must not fabricate cache-hit counts.

For streaming responses, Phase 1 records routing/latency/attempt telemetry. Usage is recorded only if the provider adapter can obtain a terminal usage payload without changing externally visible streaming semantics.

## Cost model
Provider definitions may optionally include pricing metadata for estimation when the upstream does not report cost:
- input price per million tokens
- cached-input price per million tokens
- output price per million tokens

Estimated cost is calculated only when required token counts and configured pricing are available. Telemetry clearly distinguishes `reported` from `estimated` cost.

Pricing is optional and must not affect routing in Phase 1.

## Telemetry store
Introduce a bounded in-memory telemetry collector behind an interface, keeping core routing independent from the admin UI.

A request telemetry record contains:
- timestamp
- route id
- provider id
- upstream model
- routing strategy
- pinned/sticky/fallback flags
- affinity hit/miss classification
- attempt count
- latency
- normalized usage
- reported/estimated cost
- failure kind/status for failed requests

Do not store prompts, tool contents, API keys, raw session ids, or response bodies.

The collector maintains aggregate views by route and provider plus a bounded recent-request ring buffer. Default recent retention should be count-based and configurable rather than unbounded.

## Management API
Extend authenticated management endpoints with:

- `GET /telemetry/summary` — aggregate request count, success/error counts, average latency, tokens, cached tokens, cache ratio, and cost grouped by provider/route.
- `GET /telemetry/recent` — bounded recent routing records without prompt/body data.
- `POST /probe/cache` — execute a controlled cache-affinity probe.

Existing bearer-key protection applies.

## Cache probe
`POST /probe/cache` accepts a route/model plus a small OpenAI-compatible probe request and repeat count. Default repeat count is 3; enforce a small upper bound to prevent accidental spend.

The probe sends identical requests through normal routing while forcing one generated probe session id so Sticky behavior is measurable. It returns per-attempt:
- selected provider/model
- latency
- input/output/cached tokens when available
- cost when available
- affinity classification

It also returns diagnostics:
- target changed between repeated requests
- cache data unavailable
- cache ratio remained zero when upstream exposes cached-token fields
- recommendation to use Sticky or direct `provider/model` pinning when target instability is observed

The probe reports observations only; it must not automatically change route configuration.

## Admin UI
Add a compact Cache & Cost section to the existing admin UI rather than a separate application.

Phase 1 UI shows:
- overall cache ratio
- token totals
- estimated/reported spend
- average latency
- provider/route table
- recent requests table
- cache probe form and result summary

No charting framework is required for Phase 1. Prefer simple cards/tables consistent with the existing admin UI.

## Configuration changes
Add to `AiRouterOptions`:
- `StickyAffinityTtl` default 30 minutes
- `TelemetryRecentCapacity` with a conservative bounded default
- `CacheProbeMaxRepeats` with a small default maximum

Add optional pricing fields to `ProviderDefinition`. Existing serialized provider definitions must remain valid when these fields are absent.

`RouteDefinition` continues to use the existing `Strategy` field; `Sticky` is simply a new enum value.

## Failure and edge-case behavior
- If a sticky target is deleted/disabled, the next request ignores that stale affinity and selects another eligible target.
- If all providers are cooling down, preserve current behavior of retrying the resolved set rather than making the route permanently unavailable.
- If a target rate-limits, existing cooldown rules apply and a successful fallback becomes sticky.
- A route edit that removes a target naturally invalidates matching affinity entries on next lookup; eager global invalidation is unnecessary in Phase 1.
- Probe requests respect cancellation and provider timeouts.
- Telemetry failures must never fail an otherwise successful model request.
- Cost-estimation errors result in null cost rather than request failure.

## Security and privacy
- Management telemetry/probe endpoints use the existing admin bearer-key authorization.
- Never persist or return raw `X-AiRouter-Session` values.
- Never store request/response bodies in telemetry.
- Prefix fingerprints are one-way hashes over minimal normalized stable content.
- Provider API keys remain redacted exactly as today.

## Compatibility
- `Fallback` and `RoundRobin` semantics remain unchanged.
- Existing route JSON remains valid.
- Existing provider JSON remains valid.
- Existing HTTP endpoints remain OpenAI-compatible.
- Existing `X-AiRouter-Provider` and `X-AiRouter-Model` headers remain.
- New headers are additive.
- Direct `provider/model` behavior remains pinned.

## Testing strategy
### Core routing tests
- same sticky affinity repeatedly selects the same target
- different affinity keys distribute deterministically
- expired affinity is recalculated
- disabled/deleted/cooling target is not reused
- rate limit/provider failure falls back and rebinds affinity
- invalid request does not retry/rebind
- direct provider/model remains pinned
- Fallback and RoundRobin regression tests stay unchanged

### ASP.NET Core tests
- session header precedence over request `user`
- `user` precedence over prefix fingerprint
- no raw affinity value appears in response
- new routing headers are emitted correctly
- management telemetry endpoints require authorization
- probe repeat limit and invalid payload handling

### Provider/telemetry tests
- normalize OpenAI-style usage
- normalize cached-token details when present
- missing usage leaves normalized values null
- pricing estimates cached and uncached input correctly
- telemetry collector remains bounded
- telemetry failures do not fail router requests

### Integration/regression tests
Use fake upstream providers to verify repeated requests through a Sticky route remain on one provider, then induce rate limiting/failure and verify one failover followed by stable affinity to the replacement target.

## Rollout
1. Ship Sticky as opt-in; do not change existing route defaults.
2. Add telemetry and probe so users can measure before/after behavior.
3. Document recommendation: coding-agent sessions should send `X-AiRouter-Session` with a stable conversation/session id.
4. After field data proves the behavior, separately consider whether newly-created admin routes should default to Sticky. That is explicitly not part of Phase 1.

## Success criteria
- Repeated requests carrying the same explicit session id through a Sticky route select one healthy provider/model until failover is required.
- Failover rebinds affinity without breaking current health/cooldown semantics.
- Users can see which provider handled each request and whether affinity/fallback occurred.
- When upstream cache-token fields exist, AI Router reports a truthful cache ratio and probe diagnostics.
- No prompt bodies or raw session ids are retained by telemetry.
- Existing Fallback, RoundRobin, direct pinning, and OpenAI-compatible endpoints remain backward compatible.
