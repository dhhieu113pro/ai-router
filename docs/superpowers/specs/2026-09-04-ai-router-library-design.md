# AI Router Library Design

## Goal

Create `dhhieu113pro/ai-router` as a reusable .NET 10 library for routing OpenAI-compatible AI requests across multiple provider instances, with provider management, health-aware automatic fallback, round-robin routing, and a thin optional ASP.NET Core gateway.

The repository is library-first. The standalone server and Docker image are adapters around the same public routing API used by embedded consumers.

The initial product scope is intentionally smaller than AI Studio and 9Router: routing and provider management only.

## Success Criteria

The first releasable version is complete when:

- Another .NET project can reference the core package and route requests without ASP.NET Core or SQLite dependencies.
- Multiple provider instances/accounts can be registered, enabled/disabled, prioritized, health-checked, and updated at runtime.
- Requests can use priority fallback or round-robin routing, with automatic continuation when a selected target is unavailable.
- `POST /v1/chat/completions` works for streaming and non-streaming requests.
- `POST /v1/responses` works for streaming and non-streaming requests.
- `GET /v1/models` exposes router-visible models/routes.
- Provider management is available through a protected HTTP management API in the standalone server.
- The server runs as a small Linux `amd64`/`arm64` container with no UI, Local LLM runtime, media tooling, Node, Python, ffmpeg, or yt-dlp.
- Unit and integration tests cover routing semantics and representative OpenAI-compatible requests.

## Explicit Non-Goals for v1

Do not extract or implement:

- AI Studio chat/conversation persistence.
- Local LLM / LLamaSharp / llama.cpp support.
- TTS or STT.
- MCP or tool execution.
- Skills, agents, jobs, automation, Telegram, or workspace features.
- Video/media composition.
- Angular or another UI.
- Proxy/MITM/tunnel behavior.
- Embeddings API.
- Usage billing, quotas, or token accounting beyond forwarding upstream usage data.
- Distributed routing state or Redis.

These may be separate future packages if real use cases require them.

## Design Principles

1. **Library first.** Routing logic has no dependency on ASP.NET Core, EF Core, SQLite, Docker, or AI Studio.
2. **Provider instances, not provider brands.** Two OpenRouter accounts are two independently routable provider instances.
3. **Policy is separate from transport.** A provider adapter knows how to call an upstream API; a routing strategy decides which target to try.
4. **Fallback behavior is deterministic and testable.** Request-level validation errors stop routing; target/provider failures can continue to another target.
5. **Streaming is first-class.** The router must not buffer a streaming completion simply to make routing work.
6. **Runtime configuration is abstracted.** In-memory storage works by default; SQLite is an optional persistence package.
7. **No AI Studio project references.** AI Studio is source material for behavior only.

## Solution Structure

```text
src/
  AiRouter/
  AiRouter.Providers.OpenAI/
  AiRouter.Persistence.Sqlite/
  AiRouter.AspNetCore/
  AiRouter.Server/

tests/
  AiRouter.Tests/
  AiRouter.AspNetCore.Tests/
  AiRouter.IntegrationTests/
```

### `AiRouter`

The reusable core NuGet package. It owns public contracts, provider registry/management, route definitions, routing strategies, health/cooldown state, model resolution, and dependency-injection extensions.

It references only framework-neutral .NET/Microsoft.Extensions abstractions needed for DI, logging, options, and HTTP abstractions where appropriate. It does not reference ASP.NET Core MVC or persistence implementations.

### `AiRouter.Providers.OpenAI`

The first provider adapter package. It implements generic OpenAI-compatible upstream transport and supports custom base URL, credentials, extra headers, models endpoint, chat-completions endpoint, and responses endpoint.

Special provider brands such as OpenRouter or DeepSeek should initially be configuration presets over the generic adapter unless they require genuinely different protocol behavior.

### `AiRouter.Persistence.Sqlite`

Optional provider/route configuration persistence. It implements the core storage abstractions and has no effect on consumers that do not reference it.

### `AiRouter.AspNetCore`

ASP.NET Core integration. It maps the OpenAI-compatible gateway endpoints and provider-management endpoints onto the core library.

### `AiRouter.Server`

Tiny executable host used for Docker and standalone deployment. It wires configuration, SQLite, auth, `AiRouter.AspNetCore`, and the OpenAI-compatible provider adapter together.

## Core Public Model

### Provider definition

A provider definition represents one independently routable account/endpoint.

```text
ProviderDefinition
- Id                  stable unique id, e.g. openrouter-primary
- Name                display name
- Type                adapter type, initially openai-compatible
- BaseUrl
- ApiKey
- Enabled
- Priority            lower value means earlier fallback preference
- Timeout
- Models[]             configured models when discovery is unavailable/disabled
- DiscoverModels       whether the adapter may use the upstream models endpoint
- ExtraHeaders{}
- ChatEndpoint         optional override
- ResponsesEndpoint    optional override
- ModelsEndpoint       optional override
```

`ApiKey` is write-only through management APIs: list/get responses never return the full stored secret. Updating a provider without a replacement key preserves the existing key.

SQLite v1 may persist the credential in the database; the documentation must explicitly state that the data directory must be protected. Secret-protection-at-rest is an extension point rather than a cross-platform encryption scheme baked into the core package.

### Provider runtime state

Runtime health is separate from persisted configuration:

```text
ProviderHealth
- Status              Healthy | Degraded | CoolingDown | Disabled
- ConsecutiveFailures
- CooldownUntil
- LastRequestAt
- LastSuccessAt
- LastFailureAt
- LastError
- LastLatency
```

Health state is in-memory in v1. Restarting the process resets transient cooldown state.

### Provider adapter

The core protocol is transport-agnostic:

```text
IAiProvider
- Id
- Capabilities
- SendChatAsync(...)
- SendResponsesAsync(...)
- ListModelsAsync(...)
- CheckHealthAsync(...)
```

Provider results distinguish:

- success,
- request validation failure,
- target/provider failure,
- rate limiting,
- cancellation,
- streaming response.

The routing engine consumes this classification instead of hard-coding provider-specific HTTP behavior.

## Provider Management

Core exposes an `IProviderManager` responsible for runtime provider definitions and registry refresh.

Required operations:

- list providers,
- get provider,
- add provider,
- update provider,
- delete provider,
- enable/disable provider,
- test provider connectivity,
- discover/list models,
- read current runtime health.

Changes must become visible to new requests without restarting the process.

Storage is represented by `IProviderStore`. `InMemoryProviderStore` is included in the core package. SQLite implements the same interface in `AiRouter.Persistence.Sqlite`.

The core never depends on a management HTTP API; that API is only an ASP.NET adapter.

## Routes and Model Resolution

Routing must support both direct targets and logical route aliases.

### Direct provider target

`{providerId}/{model}` pins the request to one provider instance and one upstream model.

Example:

```text
openrouter-primary/openai/gpt-4.1-mini
```

A pinned request does not silently move to another provider. This preserves caller intent.

### Provider target

`{providerId}` selects that provider and lets the provider adapter/default configuration determine the model only when such a default is explicitly configured. Otherwise the router returns a validation error.

### Logical route alias

A `RouteDefinition` gives callers a stable model name independent of provider/account details.

```text
RouteDefinition
- Id                  e.g. default, fast, coding
- Strategy            Fallback | RoundRobin
- Targets[]
    - ProviderId
    - Model
    - Priority
    - Enabled
```

Calling `/v1/chat/completions` with `model: "coding"` resolves to the `coding` route.

### `all`

`all` is a built-in route over all enabled provider/model targets. Its default strategy is priority fallback. It exists primarily for compatibility with AI Studio behavior and can be disabled later if a stricter deployment prefers named routes only.

### `/v1/models`

The models endpoint returns:

- logical route ids,
- direct provider/model ids for discoverable/configured models.

This makes the gateway usable by normal OpenAI clients without requiring them to understand a separate routing API.

## Routing Semantics

### Priority fallback

Targets are ordered by target priority, then provider priority, then stable provider id/model ordering for deterministic ties.

The router attempts targets sequentially until one succeeds or the request is classified as globally invalid.

### Round robin

Round-robin state is maintained per logical route using an atomic counter.

For each request:

1. choose the next eligible target as the starting target,
2. attempt that target,
3. if it fails with a target/provider failure, continue through the remaining eligible targets as fallback,
4. stop on success or globally invalid request.

This gives round-robin load distribution without sacrificing availability.

### Health and cooldown

Disabled providers are never eligible.

A provider enters cooldown after configurable consecutive failures or an upstream rate-limit response. Cooling-down providers are skipped while healthy alternatives exist.

If every otherwise-eligible target is cooling down, the router may make one last-resort pass over cooled-down targets instead of failing immediately.

Successful requests reset consecutive-failure state.

### Error classification

The generic OpenAI-compatible adapter uses these defaults:

- local request-shape/model-resolution validation: globally terminal; do not try another target,
- upstream HTTP 400/422 indicating invalid request: globally terminal,
- upstream 404 model/endpoint failure: target failure; another configured target may succeed,
- upstream 401/403: provider failure; mark unhealthy and allow fallback,
- upstream 408/409/429: provider/target failure; allow fallback,
- upstream 5xx: provider failure; allow fallback,
- network/connection/timeout before a response: provider failure; allow fallback,
- caller cancellation: stop immediately and propagate cancellation.

Provider adapters may refine classification when a protocol has better structured error information.

### Retry policy

v1 does not layer multiple opaque retry systems. One target gets at most one router-level attempt by default. Provider-specific transient retry can be opt-in and bounded, but automatic fallback should normally move quickly to the next target.

## Request/Response Model

The core uses normalized request/response contracts rather than ASP.NET DTOs.

### Chat Completions

Support the OpenAI-compatible request surface needed for normal clients:

- `model`,
- `messages`,
- `stream`,
- temperature/top-p/max token controls,
- stop sequences,
- tool/function definitions and tool choice,
- response format when supported,
- passthrough additional properties.

Responses preserve upstream usage and model information when available. The router records the actual provider/model used in routing metadata available to embedded consumers and optional response headers, without breaking OpenAI response JSON.

### Responses API

`POST /v1/responses` is a first-class gateway endpoint.

The provider adapter declares whether an upstream supports native Responses API. If native support is available, the adapter forwards a normalized Responses request to that endpoint. Otherwise the OpenAI-compatible adapter may translate the supported Responses subset to chat completions and translate the result back.

The initial supported Responses subset includes:

- string input,
- structured input messages/items needed for text and tool calls,
- instructions,
- model,
- stream,
- common generation controls,
- function tools and tool choice.

Unsupported Responses features return a clear OpenAI-style `invalid_request_error`; they must not be silently ignored.

### Streaming

Once an upstream streaming response begins successfully, the router does not switch providers mid-stream. Fallback is possible only before stream commitment (connection/setup/upstream status failure).

The ASP.NET adapter forwards SSE promptly and propagates caller cancellation.

## ASP.NET Core API

`AiRouter.AspNetCore` exposes extension methods similar to:

```csharp
services.AddAiRouter();
services.AddOpenAiCompatibleProvider();
services.AddAiRouterAspNetCore();

app.MapAiRouterOpenAiEndpoints();
app.MapAiRouterManagementEndpoints();
```

OpenAI-compatible surface:

```text
POST /v1/chat/completions
POST /v1/responses
GET  /v1/models
```

Management surface:

```text
GET    /providers
POST   /providers
GET    /providers/{id}
PUT    /providers/{id}
DELETE /providers/{id}
POST   /providers/{id}/enable
POST   /providers/{id}/disable
POST   /providers/{id}/test
GET    /providers/{id}/models
GET    /providers/{id}/health

GET    /routes
POST   /routes
GET    /routes/{id}
PUT    /routes/{id}
DELETE /routes/{id}
```

Management DTOs redact provider credentials.

## Authentication

Auth is a server/ASP.NET concern, not a core-library concern.

The standalone server supports two optional bearer keys:

- `AIROUTER_API_KEY` protects `/v1/*`,
- `AIROUTER_ADMIN_KEY` protects provider/route management APIs.

If `AIROUTER_ADMIN_KEY` is absent, management endpoints bind only when explicitly enabled by server configuration; Docker defaults must not expose unauthenticated provider management on a public interface.

Embedded ASP.NET consumers may replace this with their own authentication/authorization policies.

## Persistence

`AiRouter.Persistence.Sqlite` stores:

- provider definitions,
- logical route definitions and targets.

It does not store transient health/cooldown state in v1.

The standalone server uses `/data/ai-router.db` by default and supports overriding the data path.

Schema migrations are owned by the persistence package and tested against empty and existing databases.

## Configuration Sources

The standalone server supports bootstrapping providers/routes from configuration/environment and optional SQLite state.

Persistent state wins for records with the same id. Configuration-only records are usable without requiring a DB write. Management writes go to the configured `IProviderStore`/route store.

The core library itself does not assume `appsettings.json`.

## Docker

The Dockerfile publishes only `AiRouter.Server`.

Requirements:

- .NET 10 ASP.NET runtime image,
- multi-stage SDK build,
- `linux/amd64` and `linux/arm64`,
- port `8080`,
- `/data` volume,
- non-root runtime user,
- no Node/UI stage,
- no Local LLM libraries,
- no CUDA/Vulkan dependencies,
- no ffmpeg,
- no Python,
- no yt-dlp.

GitHub Actions builds/tests every PR and performs a non-push Docker build. Version tags publish `ghcr.io/dhhieu113pro/ai-router` with semver and `latest` tags.

## AI Studio Extraction Strategy

Do not copy `AIStudio.Core` wholesale.

Port behavior selectively from these areas:

- provider contracts and result concepts,
- provider factory/registration concept,
- provider configuration fields that remain routing-relevant,
- fallback/cooldown behavior from `ProviderRouter`,
- generic OpenAI-compatible provider transport,
- existing chat-completions and Responses protocol handling where it is independent of AI Studio conversations/tools.

Explicitly leave behind:

- `DataService` and `AIStudioContext`,
- Caveman integration,
- conversation-history preference logic,
- tools and agents,
- plugin discovery/generator architecture,
- full AI Studio auth/user model,
- usage/conversation logging coupled to AI Studio tables,
- local aliases such as `local`/`local-llm`.

The extraction is a clean-room refactor inside the same owner's repositories: preserve proven behavior through tests, but design new package boundaries and public types for reuse.

## Testing Strategy

### Core unit tests

Cover:

- provider registration/update/delete/enable/disable,
- duplicate and invalid ids,
- exact provider/model resolution,
- logical route resolution,
- `all` resolution,
- priority ordering,
- deterministic tie ordering,
- fallback after provider failure,
- no cross-provider fallback for pinned requests,
- round-robin rotation,
- round-robin fallback after selected-target failure,
- cooldown skip and all-cooled-down last resort,
- rate-limit cooldown,
- reset after success,
- cancellation,
- concurrent round-robin requests.

### Provider adapter tests

Use fake/loopback HTTP servers; no live paid provider calls in CI.

Cover:

- auth and custom headers,
- configurable endpoints,
- model discovery,
- non-stream chat,
- SSE chat streaming,
- native Responses forwarding,
- Responses-to-chat fallback translation,
- HTTP/error classification,
- timeout/network failures.

### ASP.NET integration tests

Use `WebApplicationFactory` with fake providers.

Cover:

- `/v1/chat/completions`,
- `/v1/responses`,
- `/v1/models`,
- management CRUD/redaction,
- runtime changes affecting subsequent routing,
- API/admin authentication,
- SSE streaming and cancellation.

### Persistence tests

Use temporary SQLite databases and verify provider/route round trips, updates, deletes, migration startup, and secret redaction at API boundaries.

### CI gates

A PR is merge-ready only when:

- `dotnet build` succeeds with warnings treated as errors for project code,
- all unit/integration tests pass,
- formatting/analyzers pass,
- package build succeeds,
- Docker `linux/amd64` build succeeds on PRs,
- release workflow definition supports `linux/amd64,linux/arm64` publishing.

## Initial Delivery Milestones

1. Bootstrap solution and CI with core contracts/tests.
2. Implement provider registry, in-memory stores, health state, and route definitions.
3. Implement fallback and round-robin routing engine.
4. Implement generic OpenAI-compatible provider adapter and chat completions.
5. Implement Responses API support and translation fallback.
6. Implement ASP.NET OpenAI-compatible endpoints.
7. Implement provider/route management endpoints and auth hooks.
8. Implement optional SQLite persistence.
9. Add standalone server, Docker, package metadata, and README examples.
10. Run full CI, inspect the Docker dependency graph/image, and prepare the PR for merge.

## Public API Stability

The first PR may ship as `0.1.0`. Public interfaces and DTOs should be small and documented. Internal routing machinery should remain internal unless an extension point is needed by consumers.

The core extension points intentionally exposed in v1 are:

- provider adapter registration,
- provider/route stores,
- routing strategy registration,
- provider result/error classification,
- ASP.NET authorization policy hooks.

Everything else should remain implementation detail until a concrete consumer requires it.
