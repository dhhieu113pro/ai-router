# AI Router Embeddable Library and Hosting Design

## Context

`ai-router` must be useful both as a standalone gateway and as a reusable .NET library embedded inside another application. The standalone `AiRouter.Server` is a reference host, not the product boundary.

This document extends the main AI Router design with the public package and hosting contract.

## Decision

The routing engine is host-agnostic. HTTP protocols, persistence, and executable hosting are optional adapters around `AiRouter`.

A consumer must be able to reference only the core package and call routing APIs directly without ASP.NET Core, SQLite, Docker, or `AiRouter.Server`.

## Package Boundary

### `AiRouter`

Core NuGet package.

Owns:

- `IAiRouter`
- provider contracts and provider management
- provider runtime health and cooldown state
- route definitions and route resolution
- priority fallback and round-robin behavior
- in-memory provider and route stores
- normalized/common request-routing metadata

Must not reference:

- ASP.NET Core hosting/MVC/minimal APIs
- EF Core or SQLite
- `AiRouter.Server`
- AI Studio

The existing low-level JSON API remains valid for protocol adapters:

```csharp
Task<RouterResult> ChatAsync(
    string model,
    JsonElement body,
    bool stream = false,
    CancellationToken ct = default);

Task<RouterResult> ResponsesAsync(
    string model,
    JsonElement body,
    bool stream = false,
    CancellationToken ct = default);
```

A later typed convenience layer may be added without changing the routing engine, but v1 does not require consumers to adopt an HTTP protocol.

### `AiRouter.Providers.OpenAI`

OpenAI-compatible upstream transport package.

Owns:

- generic OpenAI-compatible provider implementation
- upstream authentication and custom headers
- `/chat/completions`, `/responses`, and `/models` transport
- OpenAI error classification
- Responses-to-Chat compatibility translation when needed

This package describes how the router calls an upstream provider. It does not host inbound HTTP endpoints.

### `AiRouter.Persistence.Sqlite`

Optional persistence implementation for `IProviderStore` and `IRouteStore`.

Consumers that already have a database/configuration system should not need this package. They can implement the storage abstractions themselves.

### `AiRouter.AspNetCore`

Optional ASP.NET Core hosting adapter.

Owns endpoint mapping and HTTP-specific behavior. It must not own routing policy.

Target extension surface:

```csharp
builder.Services.AddAiRouter();
builder.Services.AddOpenAiCompatibleProvider();
builder.Services.AddAiRouterAspNetCore();

app.MapAiRouterOpenAiEndpoints();
app.MapAiRouterManagementEndpoints();
```

The OpenAI-compatible mapping exposes:

```text
POST /v1/chat/completions
POST /v1/responses
GET  /v1/models
```

Management endpoints remain separately mappable so embedded applications can apply their own authentication and authorization policies.

### `AiRouter.Server`

Reference executable host and Docker target.

It composes:

- `AiRouter`
- `AiRouter.Providers.OpenAI`
- `AiRouter.Persistence.Sqlite`
- `AiRouter.AspNetCore`

It does not contain routing logic that is unavailable to embedded users.

## Supported Consumption Styles

### 1. Library-only

A worker, desktop application, background service, MCP server, test host, or other .NET process can inject/use `IAiRouter` directly.

There is no HTTP listener unless the consuming application creates one.

### 2. Custom application host

An ASP.NET Core application can inject `IAiRouter` into its own controllers/minimal APIs and expose any route shape it wants, for example:

```text
/api/assistant
/api/llm/chat
/internal/generate
```

The application owns its request DTOs, authentication, authorization, response envelope, logging, and lifecycle.

### 3. OpenAI-compatible host

An application can opt into the standard OpenAI-compatible endpoint mapper so existing OpenAI SDKs and tools can use the router with only a base-URL change.

This is a convenience protocol adapter, not the core API contract.

### 4. Additional inbound protocols

Future protocol adapters such as Ollama-style endpoints, gRPC, MCP-facing tools, or custom company APIs should depend on `AiRouter`, not modify it.

For example, a future Ollama adapter could be a separate package such as `AiRouter.AspNetCore.Ollama`.

## Dependency Direction

```text
Custom application ───────┐
OpenAI HTTP adapter ──────┤
Future Ollama adapter ────┤
MCP/gRPC/custom host ─────┼──> AiRouter ──> IAiProvider implementations
Direct C# consumer ───────┘

AiRouter.Server
  ├── AiRouter.AspNetCore
  ├── AiRouter.Persistence.Sqlite
  ├── AiRouter.Providers.OpenAI
  └── AiRouter
```

Dependencies never point from `AiRouter` back toward a host or protocol adapter.

## Public API Stability

The package boundary is part of the v1 contract.

- `IAiRouter`, provider/store abstractions, routing definitions, and result metadata should be designed for consumption outside this repository.
- Server-only configuration types must not leak into core interfaces.
- ASP.NET types (`HttpContext`, `IResult`, MVC attributes) must not appear in `AiRouter` public contracts.
- EF/SQLite entity types must not appear in `AiRouter` public contracts.
- The standalone server must use the same public abstractions available to external consumers.

## OpenAI Compatibility Versus Core API

OpenAI compatibility is important because it gives immediate interoperability, but OpenAI JSON is not the architecture boundary.

The core router accepts protocol-neutral routing inputs plus payload data required by a provider adapter. The OpenAI adapter is responsible for OpenAI request/response semantics.

This prevents the project from becoming impossible to reuse when another host wants a different contract.

## Documentation Requirement

The repository must ship `docs/library-usage.md` covering:

1. package selection,
2. library-only usage,
3. custom ASP.NET hosting,
4. OpenAI-compatible hosting,
5. custom stores/providers,
6. what is optional versus required,
7. streaming disposal/cancellation responsibilities.

README should link to the guide when the public packages are ready for release.

## Testing Requirement

Add architecture/contract tests that prove:

- `AiRouter` does not reference ASP.NET Core hosting packages,
- `AiRouter` does not reference EF Core/SQLite,
- `AiRouter.Server` depends on adapters rather than owning routing implementations,
- direct `IAiRouter` usage works without creating a `WebApplication`,
- OpenAI endpoint mapping routes through the same `IAiRouter` used by direct consumers,
- a custom host can replace persistence and authentication without depending on `AiRouter.Server`.

## Non-Goals

For v1 we will not:

- create a universal protocol abstraction for every AI API,
- implement an Ollama inbound API until there is a concrete need,
- force all consumers to use SQLite,
- force all consumers to use ASP.NET Core,
- expose AI Studio-specific services through public contracts.
