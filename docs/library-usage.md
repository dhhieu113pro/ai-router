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
- fallback and round-robin routes
- provider health/cooldown behavior
- Chat Completions and Responses-style routing
- streaming
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

`AddAiRouter()` registers the routing engine, in-memory stores, provider manager, and the built-in OpenAI-compatible provider support.

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
    DefaultModel: "openai/gpt-5"), cancellationToken);
```

Provider changes are available to new requests without restarting the process.

## 3. Define a fallback route

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

Callers can now use the stable logical model name `coding`; AIRouter selects the preferred target and falls back on eligible failures.

Round-robin uses the same route model with `RoutingStrategy.RoundRobin`.

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

`RouterResult` identifies the actual selected provider/model as well as the body/stream and status information.

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

The consumer owns disposal of the returned stream. AIRouter can fall back before a stream is committed; it never switches provider after upstream streaming has been committed.

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

Your host owns URLs, auth, DTOs, telemetry, and lifecycle; AIRouter owns provider selection/routing.

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

This maps the same OpenAI-compatible endpoints at `/api/v1/chat/completions`, `/api/v1/responses`, and `/api/v1/models`. Prefixes are normalized, so `api`, `/api`, `api/`, and `/api/` behave the same.

Bearer-key protection is still available when needed:

```csharp
app.UseAiRouter("api", bearerKey: configuration["AIROUTER_API_KEY"]);
```

Management endpoints remain opt-in and separate:

```csharp
app.MapAiRouterManagementEndpoints(adminKey);
```

Your application can instead expose its own management UI/API over `IProviderManager` and `IRouteStore`.

## 8. Use your own persistence

Core defaults to in-memory providers/routes. To persist them in your existing database/configuration system, implement and register the abstractions before `AddAiRouter()`:

```csharp
builder.Services.AddSingleton<IProviderStore, MyProviderStore>();
builder.Services.AddSingleton<IRouteStore, MyRouteStore>();
builder.Services.AddAiRouter();
```

Core uses replaceable registrations, so application-owned stores stay in control.

## 9. Implement another upstream protocol

Implement `IAiProvider` and `IAiProviderFactory` when an upstream is not OpenAI-compatible. Provider adapters translate the routed JSON payload, provide streaming/non-streaming responses and model/health behavior, and classify failures so Core knows when fallback is allowed.

## 10. `AiRouter.Server` is optional

Use `AiRouter.Server` when you want the ready-made gateway/container. It composes the two public libraries with internal SQLite persistence.

Do not depend on the server project merely to access routing.

```text
Any .NET App ───────────────> AIRouter.Core
ASP.NET Core Host ──────────> AIRouter.AspNetCore ──> AIRouter.Core
AiRouter.Server ────────────> AIRouter.AspNetCore + AIRouter.Core + internal SQLite
```

Future Ollama-style, gRPC, MCP, or custom inbound protocols should be separate host adapters over Core rather than changes to the routing engine.