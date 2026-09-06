# Using AIRouter in Another .NET Project

AIRouter is library-first. `AiRouter.Server` is optional; applications can embed the router directly and choose their own host, authentication, telemetry, and persistence.

AIRouter has exactly two public NuGet packages.

## Package selection

### `AIRouter.Core` — any .NET 10 application

Install only Core when you want provider management and routing inside a console app, worker, Windows service, desktop app, MCP server, or another .NET application.

```xml
<PackageReference Include="AIRouter.Core" Version="0.1.0" />
```

Core includes:

- `IAiRouter`
- provider management through `IProviderManager`
- built-in OpenAI-compatible upstream providers
- model discovery
- fallback, round-robin, and sticky cache-affinity routes
- provider health/cooldown behavior
- Chat Completions and Responses-style routing
- streaming
- bounded in-memory routing/cache/cost telemetry
- in-memory provider and route stores by default
- replaceable `IProviderStore` and `IRouteStore` abstractions

Core does **not** reference ASP.NET Core, SQLite, EF Core, or `AiRouter.Server`.

### `AIRouter.AspNetCore` — host OpenAI-compatible `/v1` routes

Install the ASP.NET adapter when your application should expose an OpenAI-compatible API:

```xml
<PackageReference Include="AIRouter.AspNetCore" Version="0.1.0" />
```

`AIRouter.AspNetCore` depends on the matching `AIRouter.Core` version transitively, so an ASP.NET host does not need a separate Core `PackageReference` unless you prefer to declare it explicitly.

It maps:

```text
POST /v1/chat/completions
POST /v1/responses
GET  /v1/models
```

SQLite is intentionally not a public package. Core uses in-memory stores unless your application supplies its own implementations.

## 1. Register Core

```csharp
var services = new ServiceCollection();
services.AddAiRouter();
```

`AddAiRouter()` registers the routing engine, in-memory stores, provider manager, affinity store, bounded telemetry collector, and the built-in OpenAI-compatible provider support.

In an application using dependency injection, resolve the public services normally:

```csharp
var providerManager = serviceProvider.GetRequiredService<IProviderManager>();
var routeStore = serviceProvider.GetRequiredService<IRouteStore>();
var router = serviceProvider.GetRequiredService<IAiRouter>();
```

## 2. Configure an OpenAI-compatible provider

```csharp
using AiRouter.Providers;

await providerManager.AddAsync(new ProviderDefinition(
    Id: "openrouter-primary",
    Name: "OpenRouter Primary",
    Type: "openai-compatible",
    BaseUrl: "https://openrouter.ai/api/v1",
    ApiKey: configuration["OpenRouter:ApiKey"],
    Enabled: true,
    Priority: 10,
    Models: ["openai/gpt-5", "anthropic/claude-sonnet-4.6"],
    DefaultModel: "openai/gpt-5",
    InputPricePerMillion: 1.0m,
    CachedInputPricePerMillion: 0.1m,
    OutputPricePerMillion: 2.0m), cancellationToken);
```

Pricing is optional and is used only to estimate cost when upstream usage contains enough token detail and no reported cost is available. If cached input tokens are present but `CachedInputPricePerMillion` is missing, AIRouter leaves estimated cost unknown instead of guessing.

Provider changes are available to new requests without restarting the process.

## 3. Define routes

Fallback route:

```csharp
var route = new RouteDefinition(
    Id: "coding",
    Strategy: RoutingStrategy.Fallback,
    Targets:
    [
        new RouteTarget("openrouter-primary", "openai/gpt-5", Priority: 10),
        new RouteTarget("deepseek-backup", "deepseek-chat", Priority: 20)
    ]);

await routeStore.UpsertAsync(route, cancellationToken);
```

Round-robin uses the same route model with `RoutingStrategy.RoundRobin`.

For coding agents and other long conversations, prefer `RoutingStrategy.Sticky` when upstream prompt-cache locality matters:

```csharp
await routeStore.UpsertAsync(new RouteDefinition(
    Id: "coding",
    Strategy: RoutingStrategy.Sticky,
    Targets:
    [
        new RouteTarget("openrouter-a", "deepseek-v4-flash", Priority: 10),
        new RouteTarget("openrouter-b", "deepseek-v4-flash", Priority: 20)
    ]), cancellationToken);
```

Equivalent management JSON uses `strategy: 2`:

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

Sticky affinity is opt-in; existing Fallback and RoundRobin semantics are unchanged.

## 4. Call `IAiRouter` directly

No HTTP server is required:

```csharp
using System.Text.Json;
using AiRouter.Routing;

using var document = JsonDocument.Parse(
    $$"""
    {
      "model": "coding",
      "messages": [
        { "role": "user", "content": {{JsonSerializer.Serialize(prompt)}} }
      ]
    }
    """);

var result = await router.ChatAsync(
    model: "coding",
    body: document.RootElement,
    stream: false,
    ct: cancellationToken);

if (!result.Success)
    throw new InvalidOperationException(result.ErrorMessage);
```

For direct Core use with Sticky, pass an opaque stable affinity key through `RouterRequestContext`:

```csharp
var result = await router.ChatAsync(
    "coding",
    document.RootElement,
    new RouterRequestContext(
        AffinityKey: stableConversationHash,
        AffinitySource: "application"),
    stream: false,
    ct: cancellationToken);
```

`RouterResult` identifies the selected provider/model and exposes affinity/fallback/attempt metadata. Existing `IAiRouter` implementers remain source-compatible: the new context-aware interface overloads have default implementations that delegate to the legacy methods.

## 5. Streaming

```csharp
var result = await router.ChatAsync(
    model: "coding",
    body: requestJson,
    stream: true,
    ct: cancellationToken);

if (!result.Success)
    throw new InvalidOperationException(result.ErrorMessage);

if (result.Stream is not null)
{
    await using var stream = result.Stream;
    await stream.CopyToAsync(destination, cancellationToken);
}
```

The consumer owns disposal of the returned stream. AIRouter can fall back before a stream is committed; it never switches provider after upstream streaming has been committed. Streaming usage is recorded only when an adapter can expose it without changing externally visible streaming semantics.

## 6. Use your own API shape

An ASP.NET application can use Core directly without exposing OpenAI-compatible routes:

```csharp
app.MapPost("/api/assistant", async (
    AssistantRequest request,
    IAiRouter router,
    CancellationToken ct) =>
{
    using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        model = request.Model ?? "coding",
        messages = new[] { new { role = "user", content = request.Prompt } }
    }));

    var result = await router.ChatAsync(
        request.Model ?? "coding",
        document.RootElement,
        stream: false,
        ct);

    return result.Success
        ? Results.Json(result.Body)
        : Results.Problem(result.ErrorMessage, statusCode: result.StatusCode);
});
```

Your host owns URLs, auth, DTOs and lifecycle; AIRouter owns provider selection/routing and its provider-neutral routing telemetry.

## 7. Host OpenAI-compatible `/v1`

With `AIRouter.AspNetCore`, the common setup is only `AddAiRouter()` plus `UseAiRouter()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiRouter();

var app = builder.Build();
app.UseAiRouter();
app.Run();
```

Existing OpenAI-compatible SDKs can point their base URL at your application.

To host AIRouter below another path, pass a prefix:

```csharp
app.UseAiRouter("api");
```

This maps the same OpenAI-compatible endpoints at `/api/v1/chat/completions`, `/api/v1/responses`, and `/api/v1/models`.

For a Sticky route, send one stable conversation id on every turn:

```http
X-AiRouter-Session: coding-session-123
```

The ASP.NET adapter hashes this value before it reaches affinity storage. The raw header value is never returned or stored in telemetry. Identity precedence is `X-AiRouter-Session`, then OpenAI `user`, then a fingerprint of stable leading system/developer/instructions content, then deterministic route-level selection.

Successful responses include:

```text
X-AiRouter-Provider
X-AiRouter-Model
X-AiRouter-Affinity: hit|miss|route|pinned
X-AiRouter-Affinity-Source: header|user|prefix|route
X-AiRouter-Fallback: true|false
X-AiRouter-Attempts: <n>
```

Bearer-key protection is still available when needed:

```csharp
app.UseAiRouter("api", bearerKey: configuration["AIROUTER_API_KEY"]);
```

Management endpoints remain opt-in and separate:

```csharp
app.MapAiRouterManagementEndpoints(adminKey);
app.MapAiRouterConfigurationManagementEndpoints(adminKey);
app.MapAiRouterTelemetryManagementEndpoints(adminKey);
```

The telemetry extension adds authenticated:

```text
GET  /telemetry/summary
GET  /telemetry/recent
POST /probe/cache
```

`/probe/cache` repeats one small identical request using one generated affinity identity and reports selected targets, latency, available cached-token data, cost data, target instability, and a Sticky/direct-pin recommendation when appropriate. It never modifies route configuration.

Telemetry is bounded and deliberately excludes prompts, tool output, response bodies, API keys, raw session ids, and affinity keys.

## 8. Upstream aggregators and cache locality

Sticky can only control the provider/model target visible to AIRouter. If one configured provider is itself a gateway that load-balances the same model across multiple hidden workers, those internal hops can still defeat provider-local prompt caches. In that case use the upstream gateway's provider/backend pinning feature as well, or model the pinned backends as separate AIRouter provider definitions.

This distinction is important for gateways such as multi-provider aggregators: `openrouter-a/deepseek-v4-flash` is stable from AIRouter's perspective only if the upstream configuration behind `openrouter-a` is also stable enough for its cache semantics.

## 9. Use your own persistence

Core defaults to in-memory providers/routes and in-memory affinity/telemetry. To persist provider/route configuration in your existing database/configuration system, implement and register the abstractions before `AddAiRouter()`:

```csharp
builder.Services.AddSingleton<IProviderStore, MyProviderStore>();
builder.Services.AddSingleton<IRouteStore, MyRouteStore>();
builder.Services.AddAiRouter();
```

Core uses replaceable registrations, so application-owned stores stay in control. Phase 1 affinity is intentionally in-memory and may reset on process restart.

## 10. Implement another upstream protocol

Implement `IAiProvider` and `IAiProviderFactory` when an upstream is not OpenAI-compatible. Provider adapters translate the routed JSON payload, provide streaming/non-streaming responses and model/health behavior, and classify failures so Core knows when fallback is allowed.

## 11. `AiRouter.Server` is optional

Use `AiRouter.Server` when you want the ready-made gateway/container. It composes the two public libraries with internal SQLite persistence and the Angular Cache & Cost admin view.

Do not depend on the server project merely to access routing.

```text
Any .NET App ───────────────> AIRouter.Core
ASP.NET Core Host ──────────> AIRouter.AspNetCore ──> AIRouter.Core
AiRouter.Server ────────────> AIRouter.AspNetCore + AIRouter.Core + internal SQLite
```

Future Ollama-style, gRPC, MCP, or custom inbound protocols should be separate host adapters over Core rather than changes to the routing engine.
