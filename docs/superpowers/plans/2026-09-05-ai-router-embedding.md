# Embeddable AiRouter Package Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing `AiRouter` package a stable host-agnostic library that another .NET application can consume directly, while keeping ASP.NET Core, SQLite, OpenAI inbound HTTP, and the standalone server optional adapters.

**Architecture:** `AiRouter` remains the only routing-policy package. `AiRouter.Providers.OpenAI` handles upstream OpenAI-compatible transport, `AiRouter.AspNetCore` maps optional inbound HTTP endpoints, `AiRouter.Persistence.Sqlite` implements optional stores, and `AiRouter.Server` is only a reference composition root. Direct C# consumers and custom hosts use the same `IAiRouter`, `IProviderManager`, `IProviderStore`, and `IRouteStore` contracts as the standalone server.

**Tech Stack:** .NET 10, Microsoft.Extensions.DependencyInjection, System.Text.Json, ASP.NET Core minimal APIs in the adapter package only, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-04-ai-router-embedding-design.md`

## Global Constraints

- `AiRouter` must remain usable without creating a `WebApplication`.
- `AiRouter` must not reference ASP.NET Core hosting/MVC/minimal APIs, EF Core, SQLite, `AiRouter.Server`, Docker, or AI Studio.
- `AiRouter.Server` must not contain routing behavior unavailable through public core abstractions.
- Inbound OpenAI compatibility is optional and implemented in `AiRouter.AspNetCore`; upstream OpenAI-compatible transport stays in `AiRouter.Providers.OpenAI`.
- Embedded consumers own their URL shape, authentication, authorization, persistence, telemetry, and application lifecycle.
- `AiRouter.Persistence.Sqlite` is optional; custom applications can replace `IProviderStore` and `IRouteStore`.
- Streaming consumers must dispose returned streams and propagate cancellation.
- Additional inbound protocols such as Ollama, gRPC, or MCP are future adapters over `AiRouter`, not core changes.

---

### Task 1: Lock the host-agnostic public API boundary

**Files:**
- Modify: `tests/AiRouter.Tests/ArchitectureTests.cs`
- Modify/Create: `tests/AiRouter.Tests/EmbeddedUsageTests.cs`
- Modify: `src/AiRouter/DependencyInjection.cs`
- Modify only if needed for DI completeness: `src/AiRouter/Routing/IAiRouter.cs`, provider/store registration files

**Interfaces:**
- Consumes: `IAiRouter.ChatAsync(string, JsonElement, bool, CancellationToken)`, `IAiRouter.ResponsesAsync(string, JsonElement, bool, CancellationToken)`, `IProviderManager`, `IProviderStore`, `IRouteStore`.
- Produces: `IServiceCollection AddAiRouter(this IServiceCollection services, Action<AiRouterOptions>? configure = null)` registering the core router with in-memory stores by default and no host requirement.

- [ ] **Step 1: Write failing architecture tests**

Add assertions that `src/AiRouter/AiRouter.csproj` contains no `FrameworkReference Include="Microsoft.AspNetCore.App"`, no `Microsoft.AspNetCore.*` package references, no EF/SQLite package references, and no project reference to `AiRouter.Server`.

- [ ] **Step 2: Write failing direct-consumption test**

Create a `ServiceCollection`, call `services.AddAiRouter()`, register a fake `IAiProviderFactory`, build the provider, add one provider and route, resolve `IAiRouter`, and call `ChatAsync` successfully without constructing `WebApplication`, `HttpContext`, SQLite, or `AiRouter.Server`.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

```bash
dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj -c Release --filter "FullyQualifiedName~ArchitectureTests|FullyQualifiedName~EmbeddedUsageTests"
```

Expected: FAIL only where the DI/public-boundary behavior is not yet complete.

- [ ] **Step 4: Implement the minimal core DI surface**

`AddAiRouter` must register `AiRouterOptions`, `InMemoryProviderStore`, `InMemoryRouteStore`, provider manager, route resolver, health state, and `IAiRouter` without registering ASP.NET or SQLite types.

- [ ] **Step 5: Re-run focused tests and verify PASS**

Run the same command and require all tests green.

- [ ] **Step 6: Commit**

```bash
git add src/AiRouter tests/AiRouter.Tests
git commit -m "feat: expose host-agnostic AiRouter DI"
```

### Task 2: Prove custom hosting and OpenAI hosting use the same core router

**Files:**
- Modify: `src/AiRouter.AspNetCore/DependencyInjection.cs`
- Modify: `src/AiRouter.AspNetCore/EndpointRouteBuilderExtensions.cs`
- Modify/Create: `tests/AiRouter.AspNetCore.Tests/EmbeddedHostingTests.cs`

**Interfaces:**
- Consumes: the `IAiRouter` registration from Task 1.
- Produces: `AddAiRouterAspNetCore()`, `MapAiRouterOpenAiEndpoints()`, and separately mappable `MapAiRouterManagementEndpoints()` that depend on injected core services rather than constructing their own router/store graph.

- [ ] **Step 1: Write failing custom-host test**

Create a minimal test app that calls `AddAiRouter()`, injects a fake `IAiRouter` implementation, maps a custom `/api/assistant` endpoint itself, and proves the custom endpoint can use the router without referencing `AiRouter.Server`.

- [ ] **Step 2: Write failing OpenAI-adapter identity test**

Register one fake `IAiRouter` singleton, call `AddAiRouterAspNetCore()`, map `MapAiRouterOpenAiEndpoints()`, issue `POST /v1/chat/completions`, and assert the exact registered fake router instance received the request. The adapter must not create a second router.

- [ ] **Step 3: Run adapter tests and verify RED**

```bash
dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj -c Release --filter "FullyQualifiedName~EmbeddedHostingTests"
```

- [ ] **Step 4: Implement minimal adapter wiring**

Keep HTTP parsing/result-writing in `AiRouter.AspNetCore`; resolve `IAiRouter`, `IProviderManager`, and stores from DI. Do not add routing policy to endpoint files.

- [ ] **Step 5: Re-run focused tests and verify PASS**

Run the same command and require green.

- [ ] **Step 6: Commit**

```bash
git add src/AiRouter.AspNetCore tests/AiRouter.AspNetCore.Tests
git commit -m "feat: support embedded ASP.NET hosting"
```

### Task 3: Package/document the embeddable contract

**Files:**
- Modify: `src/AiRouter/AiRouter.csproj`
- Modify as needed: other package `.csproj` files
- Modify: `README.md`
- Verify: `docs/library-usage.md`
- Modify/Create: `tests/AiRouter.Tests/PackagingTests.cs` or documentation contract tests

**Interfaces:**
- Consumes: public APIs proven in Tasks 1-2.
- Produces: NuGet-ready package metadata and discoverable docs explaining the four package roles.

- [ ] **Step 1: Write failing package/documentation tests**

Assert `AiRouter.csproj` is packable with package id `AiRouter`, targets `net10.0`, and contains no ASP.NET/SQLite dependency. Assert README links to `docs/library-usage.md` and names the library-only, custom-host, and OpenAI-compatible-host usage paths.

- [ ] **Step 2: Run focused tests and verify RED**

```bash
dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj -c Release --filter "FullyQualifiedName~PackagingTests|FullyQualifiedName~Documentation"
```

- [ ] **Step 3: Add package metadata and README links**

`AiRouter.csproj` should set `PackageId` to `AiRouter`, include description/repository/license metadata, and stay free of host/persistence dependencies. README must explicitly state that `AiRouter.Server` is optional and link to `docs/library-usage.md`.

- [ ] **Step 4: Verify packability**

Run:

```bash
dotnet pack src/AiRouter/AiRouter.csproj -c Release --no-build
```

Expected: one `AiRouter.*.nupkg` produced successfully with no ASP.NET Core/EF Core/SQLite dependency introduced by the core project.

- [ ] **Step 5: Re-run tests and verify PASS**

Run the focused tests plus the full `AiRouter.Tests` project.

- [ ] **Step 6: Commit**

```bash
git add src/AiRouter/AiRouter.csproj README.md docs/library-usage.md tests/AiRouter.Tests
git commit -m "docs: package AiRouter for embedded hosts"
```

### Task 4: Integrate with the existing extraction plan and final verification

**Files:**
- Existing main plan: `docs/superpowers/plans/2026-09-04-ai-router-library.md`
- Existing PR branch: `feat/extract-ai-router-library`

- [ ] **Step 1: Continue remaining tasks from the main implementation plan**

Complete Responses compatibility, SQLite persistence, ASP.NET endpoints, secure management API, standalone server, Docker/CI, and documentation.

- [ ] **Step 2: Run the complete verification matrix**

```bash
dotnet restore AiRouter.slnx
dotnet build AiRouter.slnx -c Release --no-restore
dotnet test AiRouter.slnx -c Release --no-build
dotnet pack src/AiRouter/AiRouter.csproj -c Release --no-build
docker build -t ai-router:pr .
```

Docker is allowed to be verified by GitHub Actions when unavailable locally, but .NET restore/build/test/pack must be green before completion.

- [ ] **Step 3: Verify forbidden dependency references**

Search `src/AiRouter` for `AspNetCore`, `EntityFramework`, `Sqlite`, `AIStudio`, `LLamaSharp`, and `AiRouter.Server`; only documentation/comments specifically explaining the prohibition may mention them.

- [ ] **Step 4: Run Superpowers completion/review gates**

Invoke `verification-before-completion` and `requesting-code-review`; fix substantive findings with regression tests before claiming readiness.

- [ ] **Step 5: Verify exact PR head in GitHub Actions**

All required .NET and Docker workflows must be green on the exact head SHA. Stop before merge and report the PR as ready for merge.
