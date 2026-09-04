# Using AiRouter in Another .NET Project

AiRouter is designed to work as a library first. `AiRouter.Server` is optional.

Use the smallest package set that matches how you want to host the router.

> The examples in this document define the public API target for the current v1 extraction PR. Where an extension method is not yet implemented on the branch, the implementation should converge on this documented surface rather than coupling consumers to `AiRouter.Server`.

## Package Selection

### Core routing only

Use this when your application already owns hosting, configuration, persistence, and authentication.

```xml
<PackageReference Include="AiRouter" Version="1.0.0" />
```

This gives you:

- `IAiRouter`
- provider management abstractions
- fallback and round-robin routing
- health/cooldown behavior
- route definitions
- in-memory stores

It does **not** start an HTTP server.

### OpenAI-compatible upstream providers

Add this when you want AiRouter to call OpenAI-compatible providers such as OpenAI, OpenRouter, DeepSeek, or another compatible endpoint.

```xml
<PackageReference Include="AiRouter" Version="1.0.0" />
<PackageReference Include="AiRouter.Providers.OpenAI" Version="1.0.0" />
```

### ASP.NET Core OpenAI-compatible host

Add this when your own ASP.NET Core project should expose `/v1/...` endpoints.

```xml
<PackageReference Include="AiRouter" Version="1.0.0" />
<PackageReference Include="AiRouter.Providers.OpenAI" Version="1.0.0" />
<PackageReference Include="AiRouter.AspNetCore" Version="1.0.0" />
```

### SQLite persistence

Optional:

```xml
<PackageReference Include="AiRouter.Persistence.Sqlite" Version="1.0.0" />
```

Do not reference SQLite when your application already has its own provider/route store.

## 1. Use AiRouter Directly From C#

Your application can call `IAiRouter` without ASP.NET Core.

The current low-level contract routes protocol payloads as JSON:

```csharp
using System.Text.Json;
using AiRouter.Routing;

public sealed class AssistantService(IAiRouter router)
{
    public async Task<string?> AskAsync(string prompt, CancellationToken ct)
    {
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
            ct: ct);

        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage);

        return result.Body?.ToString();
    }
}
```

No `WebApplication`, controller, SQLite database, or `AiRouter.Server` is required.

The returned `RouterResult` also identifies the actual provider/model selected by the router.

## 2. Configure Providers in Your Application

A provider represents one independently routable account/endpoint.

```csharp
using AiRouter.Providers;

var provider = new ProviderDefinition(
    Id: "openrouter-primary",
    Name: "OpenRouter Primary",
    Type: "openai-compatible",
    BaseUrl: "https://openrouter.ai/api/v1",
    ApiKey: configuration["OpenRouter:ApiKey"],
    Enabled: true,
    Priority: 10,
    Models: ["openai/gpt-5", "anthropic/claude-sonnet-4.6"],
    DefaultModel: "openai/gpt-5");
```

The provider can then be registered through `IProviderManager`.

Target DI surface:

```csharp
builder.Services
    .AddAiRouter()
    .AddOpenAiCompatibleProvider();
```

After resolving `IProviderManager`:

```csharp
await providerManager.AddAsync(provider, cancellationToken);
```

Provider changes must become visible to new requests without restarting the application.

## 3. Define Fallback Routes

A logical route lets callers use a stable model name while providers/models can change behind it.

Example intent:

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

A request with:

```json
{
  "model": "coding"
}
```

tries the preferred target first and automatically falls back on eligible provider/target failures.

## 4. Define a Round-Robin Route

```csharp
var route = new RouteDefinition(
    Id: "balanced",
    Strategy: RoutingStrategy.RoundRobin,
    Targets:
    [
        new RouteTarget("account-a", "model-x", Priority: 10),
        new RouteTarget("account-b", "model-x", Priority: 10)
    ]);
```

Each request starts with the next eligible target. If that target fails with a routable failure, the router continues through the remaining targets.

## 5. Host Your Own API Shape

You do not need OpenAI-compatible inbound endpoints.

Your ASP.NET Core application can inject `IAiRouter` into any endpoint:

```csharp
app.MapPost("/api/assistant", async (
    AssistantRequest request,
    IAiRouter router,
    CancellationToken ct) =>
{
    using var document = JsonDocument.Parse(
        JsonSerializer.Serialize(new
        {
            model = request.Model ?? "coding",
            messages = new[]
            {
                new { role = "user", content = request.Prompt }
            }
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

Your application owns:

- URL design
- authentication/authorization
- request DTOs
- response envelope
- telemetry
- lifecycle

AiRouter only performs provider selection and routing.

## 6. Host OpenAI-Compatible `/v1` Endpoints

If you want existing OpenAI clients to use your application by changing only the base URL, add the ASP.NET adapter.

Target setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAiRouter()
    .AddOpenAiCompatibleProvider()
    .AddAiRouterAspNetCore();

var app = builder.Build();

app.MapAiRouterOpenAiEndpoints();

app.Run();
```

This maps:

```text
POST /v1/chat/completions
POST /v1/responses
GET  /v1/models
```

Existing OpenAI-compatible SDKs can then point their base URL at your application.

Provider/route management endpoints are intentionally mapped separately:

```csharp
app.MapAiRouterManagementEndpoints();
```

This allows your application to protect them with its own authentication/authorization policy.

## 7. Use Your Own Provider Store

Persistence is an abstraction, not a requirement.

Implement `IProviderStore` when providers should live in your application's existing database/configuration system:

```csharp
public sealed class MyProviderStore : IProviderStore
{
    // Implement list/get/upsert/delete using your own persistence.
}
```

Register it instead of the default store:

```csharp
builder.Services.AddSingleton<IProviderStore, MyProviderStore>();
```

Do the same for `IRouteStore` when route definitions belong in your existing persistence layer.

## 8. Implement Your Own Upstream Provider

Implement `IAiProvider` when the upstream is not OpenAI-compatible.

The provider adapter is responsible for:

- converting the routed request into the upstream protocol
- returning streaming or non-streaming data
- listing models when supported
- health checks
- classifying failures so the router knows whether fallback is allowed

The router remains unaware of provider-specific HTTP/protocol details.

## 9. Streaming

For streaming calls:

```csharp
var result = await router.ChatAsync(
    model: "coding",
    body: requestJson,
    stream: true,
    ct: cancellationToken);

if (result.Stream is not null)
{
    await using var stream = result.Stream;
    await stream.CopyToAsync(destination, cancellationToken);
}
```

The consumer must dispose the returned stream.

After an upstream stream is committed, AiRouter does not switch to another provider mid-stream. Fallback occurs only before stream commitment.

Always propagate the caller's `CancellationToken`.

## 10. `AiRouter.Server` Is Optional

Use `AiRouter.Server` when you want the ready-made gateway/container.

Do **not** depend on `AiRouter.Server` from another application merely to access routing.

The intended dependency direction is:

```text
Your App ───────────────> AiRouter
Your ASP.NET Host ──────> AiRouter.AspNetCore ──> AiRouter
OpenAI Provider ────────> AiRouter.Providers.OpenAI ──> AiRouter
SQLite Persistence ─────> AiRouter.Persistence.Sqlite ──> AiRouter
AiRouter.Server ─────────> all optional adapters above
```

## Future Inbound Protocols

An Ollama-style API, gRPC host, MCP integration, or another protocol should be implemented as another adapter over `AiRouter`.

It should not require changes to the core routing engine unless the protocol reveals a genuinely new routing capability.
