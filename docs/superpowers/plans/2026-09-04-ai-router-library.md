# AI Router Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `dhhieu113pro/ai-router` as a reusable .NET 10 routing library with provider management, fallback/round-robin strategies, OpenAI-compatible `/v1/chat/completions` and `/v1/responses`, optional SQLite persistence, and a tiny standalone server/container.

**Architecture:** Keep routing and provider management in a framework-neutral `AiRouter` package. Put generic OpenAI-compatible upstream transport in `AiRouter.Providers.OpenAI`, persistence in `AiRouter.Persistence.Sqlite`, HTTP endpoints/auth in `AiRouter.AspNetCore`, and deployment wiring in `AiRouter.Server`. Preserve useful AI Studio routing behavior while removing AI Studio-specific data, conversation, plugin, Local LLM, media, and tool dependencies.

**Tech Stack:** .NET 10, Microsoft.Extensions.DependencyInjection/Logging/Options, ASP.NET Core minimal APIs, HttpClient, System.Text.Json, EF Core SQLite, xUnit, Microsoft.AspNetCore.Mvc.Testing, GitHub Actions, Docker Buildx.

**Spec:** `docs/superpowers/specs/2026-09-04-ai-router-library-design.md`

## Global Constraints

- Target .NET 10.
- `AiRouter` must not reference ASP.NET Core MVC, EF Core, SQLite, Docker, or AI Studio projects.
- No AI Studio project references anywhere in this repository.
- No Local LLM / LLamaSharp / llama.cpp, TTS/STT, MCP, tools, agents, automation, Telegram, media, Angular/UI, Node, Python, ffmpeg, or yt-dlp in v1.
- Provider instances are independently routable; multiple accounts for one provider brand must work.
- Direct `{providerId}/{model}` requests are pinned and do not silently cross-provider fallback.
- Logical routes support `Fallback` and `RoundRobin`; round robin falls through remaining targets on target/provider failure.
- Streaming may fallback only before stream commitment; never switch provider after SSE output starts.
- Core default storage is in-memory; SQLite is optional.
- Standalone management APIs are disabled unless `AIROUTER_ADMIN_KEY` is configured.
- Docker publishes only `AiRouter.Server` for `linux/amd64` and `linux/arm64`.
- Every behavior is implemented TDD-first and every PR must have green .NET tests plus a non-push Docker build.

---

### Task 1: Bootstrap solution and architecture boundaries

**Files:** Create `AiRouter.slnx`, `Directory.Build.props`, all five `src/*/*.csproj` projects, all test projects, and `tests/AiRouter.Tests/ArchitectureTests.cs`.

**Produces:** Project graph: `AiRouter.Providers.OpenAI -> AiRouter`, `AiRouter.Persistence.Sqlite -> AiRouter`, `AiRouter.AspNetCore -> AiRouter`, `AiRouter.Server -> AiRouter.AspNetCore + AiRouter.Persistence.Sqlite + AiRouter.Providers.OpenAI`.

- [ ] Write failing architecture tests that read project XML and reject `Microsoft.AspNetCore`, EF Core, SQLite, and `AIStudio` from `src/AiRouter/AiRouter.csproj`; also require the server to reference only the intended adapters.
- [ ] Run `dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj` and verify RED because projects are absent.
- [ ] Create the minimal .NET 10 project graph with nullable/implicit usings enabled and warnings-as-errors for repository code.
- [ ] Run `dotnet build AiRouter.slnx -c Release` and the architecture tests; verify PASS.
- [ ] Commit `build: bootstrap AI Router solution`.

### Task 2: Provider contracts and management

**Files:** `src/AiRouter/Providers/*`, `src/AiRouter/DependencyInjection.cs`, `tests/AiRouter.Tests/ProviderManagerTests.cs`.

**Produces:** `ProviderDefinition`, `ProviderHealth`, `ProviderResult`, `IAiProvider`, `IAiProviderFactory`, `IProviderStore`, `InMemoryProviderStore`, `IProviderManager`, `ProviderManager`.

```csharp
public sealed record ProviderDefinition(
    string Id, string Name, string Type, string BaseUrl, string? ApiKey,
    bool Enabled = true, int Priority = 100, TimeSpan? Timeout = null,
    IReadOnlyList<string>? Models = null, string? DefaultModel = null,
    bool DiscoverModels = true, IReadOnlyDictionary<string,string>? ExtraHeaders = null,
    string? ChatEndpoint = null, string? ResponsesEndpoint = null, string? ModelsEndpoint = null);
```

- [ ] Write failing tests for add/list/get/update/delete, enable/disable, duplicate/invalid ids, update-without-key preservation, runtime factory refresh, connectivity test delegation, and model discovery delegation.
- [ ] Run the provider-manager test filter and verify RED.
- [ ] Implement a case-insensitive in-memory store, id validation `^[a-z0-9][a-z0-9._-]*$`, atomic manager mutations, immutable runtime provider snapshots, and secret-preserving updates.
- [ ] Re-run focused tests and verify PASS.
- [ ] Commit `feat: add provider management core`.

### Task 3: Route definitions and model resolution

**Files:** `src/AiRouter/Routing/RoutingStrategy.cs`, `RouteDefinition.cs`, `IRouteStore.cs`, `InMemoryRouteStore.cs`, `RouteResolver.cs`, `tests/AiRouter.Tests/RouteResolverTests.cs`.

```csharp
public enum RoutingStrategy { Fallback, RoundRobin }
public sealed record RouteTarget(string ProviderId, string Model, int Priority = 100, bool Enabled = true);
public sealed record RouteDefinition(string Id, RoutingStrategy Strategy, IReadOnlyList<RouteTarget> Targets, bool Enabled = true);
public sealed record ResolvedTarget(string ProviderId, string Model);
public sealed record ResolvedRoute(string RouteId, RoutingStrategy Strategy, bool Pinned, IReadOnlyList<ResolvedTarget> Targets);
```

- [ ] Write failing tests proving: `provider/model` is pinned; `provider` requires `DefaultModel`; route alias resolves configured targets; `all` covers enabled provider/models; unknown values return invalid-request errors; ordering is target priority, provider priority, provider id, model.
- [ ] Run `RouteResolverTests`; verify RED.
- [ ] Implement in-memory route store and resolver. `all` is virtual, not persisted.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add route resolution`.

### Task 4: Routing engine

**Files:** `src/AiRouter/Configuration/AiRouterOptions.cs`, `src/AiRouter/Routing/IAiRouter.cs`, `AiRouterService.cs`, health/result types, `tests/AiRouter.Tests/AiRouterServiceTests.cs`.

- [ ] Write failing tests for fallback order, 5xx/401/403/429 fallback, terminal validation, pinned behavior, round-robin rotation, round-robin fallback, cooldown skip, all-cooled last resort, health reset, cancellation, stream commitment, and concurrent round-robin safety.
- [ ] Run `AiRouterServiceTests`; verify RED.
- [ ] Implement per-route atomic round-robin counters, per-provider health, configurable 30s error / 60s rate-limit cooldown defaults, and structured `ProviderFailureKind` classification consumed by core.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add fallback and round robin router`.

### Task 5: Normalized OpenAI contracts

**Files:** `src/AiRouter/Models/*`, `tests/AiRouter.Tests/OpenAiModelContractTests.cs`.

- [ ] Write failing JSON contract tests for model/messages/stream, generation controls, tools/tool choice, response format, Responses input/instructions, and unknown-property passthrough.
- [ ] Run focused tests; verify RED.
- [ ] Implement `System.Text.Json` DTOs with `JsonPropertyName` and `JsonExtensionData` so unsupported-but-forwardable properties are preserved.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add OpenAI protocol contracts`.

### Task 6: Generic OpenAI-compatible provider

**Files:** `src/AiRouter.Providers.OpenAI/*`, `tests/AiRouter.Providers.OpenAI.Tests/OpenAiCompatibleProviderTests.cs`.

- [ ] Write loopback HTTP tests for bearer auth, extra headers, endpoint overrides, JSON completion, SSE passthrough, model discovery, health check, 400/422 terminal errors, 404 target failure, 401/403 provider failure, 408/409/429 fallback-eligible errors, 5xx, timeout, connection failure, and cancellation.
- [ ] Run provider-adapter tests; verify RED.
- [ ] Implement transport using `IHttpClientFactory` and `HttpCompletionOption.ResponseHeadersRead`; never buffer streaming bodies.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add OpenAI compatible provider`.

### Task 7: Responses API native and compatibility modes

**Files:** `OpenAiResponsesTranslator.cs`, provider adapter updates, `OpenAiResponsesTranslatorTests.cs`.

- [ ] Write failing tests for native `/v1/responses`, string/structured input translation, instructions, function tools/tool choice, common controls, non-stream response conversion, supported SSE conversion, and explicit rejection of unsupported Responses features.
- [ ] Run translation tests; verify RED.
- [ ] Implement `SupportsNativeResponses`; native mode forwards, compatibility mode translates to chat-completions and maps the supported subset back to valid Responses JSON/SSE.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: support Responses API routing`.

### Task 8: Optional SQLite persistence

**Files:** `src/AiRouter.Persistence.Sqlite/*`, `tests/AiRouter.Persistence.Sqlite.Tests/SqliteStoreTests.cs`.

- [ ] Write failing temp-DB tests for empty creation, provider/route round trips, updates/deletes, API-key persistence, reopen/restart persistence, and manager-level key preservation.
- [ ] Run persistence tests; verify RED.
- [ ] Implement EF Core SQLite stores; serialize provider models/headers as JSON; normalize route targets; do not persist health/cooldown.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add SQLite persistence`.

### Task 9: ASP.NET OpenAI endpoints

**Files:** `src/AiRouter.AspNetCore/DependencyInjection.cs`, `EndpointRouteBuilderExtensions.cs`, `OpenAiEndpoints.cs`, `tests/AiRouter.AspNetCore.Tests/OpenAiEndpointTests.cs`.

- [ ] Write failing integration tests for `POST /v1/chat/completions`, `POST /v1/responses`, `GET /v1/models`, aliases, pinned routing, fallback, round robin, OpenAI-style errors, selected-target headers, SSE, and cancellation.
- [ ] Run endpoint tests; verify RED.
- [ ] Implement minimal APIs and OpenAI error JSON. Stream `text/event-stream` promptly and never switch provider after commitment.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: expose OpenAI compatible endpoints`.

### Task 10: Management API and auth

**Files:** `ApiKeyAuth.cs`, `ManagementEndpoints.cs`, endpoint mapping updates, `ManagementEndpointTests.cs`, `ServerAuthenticationTests.cs`.

- [ ] Write failing tests for provider CRUD, enable/disable, test/models/health, route CRUD, credential redaction, update-without-key preservation, `/v1/*` API-key auth, admin-key auth, and complete absence of management routes without an admin key.
- [ ] Run management/auth tests; verify RED.
- [ ] Implement bearer-key checks using `CryptographicOperations.FixedTimeEquals`; never log/return provider secrets.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add secure provider management API`.

### Task 11: Standalone server

**Files:** `src/AiRouter.Server/Program.cs`, `appsettings.json`, DI wiring, server integration tests.

- [ ] Write failing startup/config tests for empty DB startup, `/v1/models`, secure management default, config bootstrap, DB-over-config precedence, and management persistence across restart.
- [ ] Run server tests; verify RED.
- [ ] Wire core, OpenAI factory, SQLite, ASP.NET endpoints, DB initialization, bootstrap, API key auth, and conditional management only.
- [ ] Re-run tests; verify PASS.
- [ ] Commit `feat: add standalone AI Router server`.

### Task 12: Docker and GitHub Actions

**Files:** `Dockerfile`, `.dockerignore`, `.github/workflows/ci.yml`, `.github/workflows/container.yml`, `tests/AiRouter.Tests/PackagingTests.cs`.

- [ ] Write failing packaging tests requiring only `AiRouter.Server` publish, `AiRouter.Server.dll`, ASP.NET 10 runtime, port 8080, `/data`, non-root, no Node/Python/ffmpeg/yt-dlp/LLamaSharp/CUDA/Vulkan/AIStudio, PR non-push build, and tag multi-arch publish.
- [ ] Run packaging tests; verify RED.
- [ ] Add minimal Dockerfile and workflows. PR builds `linux/amd64`; release tags publish `linux/amd64,linux/arm64` to `ghcr.io/dhhieu113pro/ai-router` with semver + latest.
- [ ] Run tests and Docker build where available; verify PASS or record Docker limitation for Actions verification.
- [ ] Commit `ci: add test and container gates`.

### Task 13: Documentation and package metadata

**Files:** `README.md`, `LICENSE`, project/package metadata, documentation contract tests.

- [ ] Write failing README contract tests requiring embedded `services.AddAiRouter`, OpenAI endpoints, provider management, fallback/round-robin examples, API/admin keys, SQLite data protection warning, Docker usage, and no Local LLM claim.
- [ ] Run docs tests; verify RED.
- [ ] Add README and package metadata, including two-provider/account examples and `coding` fallback / `balanced` round-robin routes.
- [ ] Run the full test suite; verify PASS.
- [ ] Commit `docs: document reusable AI Router`.

### Task 14: Verification, review, PR, CI repair loop

- [ ] Run `dotnet restore AiRouter.slnx`, `dotnet build AiRouter.slnx -c Release --no-restore`, `dotnet test AiRouter.slnx -c Release --no-build`, and `docker build -t ai-router:pr .` where Docker is available.
- [ ] Search production code for unintended `AIStudio`, `LLamaSharp`, `ffmpeg`, `yt-dlp`, `python`, or `node` references.
- [ ] Invoke Superpowers `verification-before-completion` and `requesting-code-review` on the exact branch head; fix substantive findings TDD-first.
- [ ] Open PR titled `feat: extract reusable AI Router library` against `main` with architecture, routing semantics, provider-management, OpenAI compatibility, Docker, and verification evidence.
- [ ] Inspect GitHub Actions for the exact PR head. For each failure, read the failing job log, fix the root cause with a focused regression test where applicable, push, and repeat against the new head.
- [ ] Stop only when all required workflows are green and the PR is merge-ready. Do not merge. Report PR URL, exact head SHA, test results, and green workflow evidence.
