# AIRouter

AIRouter is a library-first AI provider router for .NET 10. Use it directly from any .NET application, optionally expose an OpenAI-compatible `/v1` API from your own ASP.NET Core host, or run the ready-made `AiRouter.Server` gateway/container.

## Packages

There are exactly two public NuGet packages.

### AIRouter.Core

Use `AIRouter.Core` in console apps, workers, Windows services, desktop apps, MCP servers, or any other .NET project. Core includes provider management, built-in OpenAI-compatible upstream providers, fallback and round-robin routing, Responses support, streaming, health/cooldown behavior, and in-memory provider/route stores.

```xml
<PackageReference Include="AIRouter.Core" Version="0.1.0" />
```

```csharp
var services = new ServiceCollection();
services.AddAiRouter();

await using var serviceProvider = services.BuildServiceProvider();
var providers = serviceProvider.GetRequiredService<IProviderManager>();
var router = serviceProvider.GetRequiredService<IAiRouter>();
```

SQLite is not required. Applications can keep the default in-memory stores or replace `IProviderStore` and `IRouteStore` with their own persistence.

### AIRouter.AspNetCore

Add `AIRouter.AspNetCore` when your application should expose the OpenAI-compatible API. It depends on `AIRouter.Core` and only adds ASP.NET Core hosting integration.

```xml
<PackageReference Include="AIRouter.AspNetCore" Version="0.1.0" />
```

```csharp
builder.Services
    .AddAiRouter()
    .AddAiRouterAspNetCore();

var app = builder.Build();
app.MapAiRouterOpenAiEndpoints();
```

This maps:

- `POST /v1/chat/completions`
- `POST /v1/responses`
- `GET /v1/models`

See [docs/library-usage.md](docs/library-usage.md) for provider, route, custom-host, and streaming examples.

## Optional standalone server

`AiRouter.Server` is optional. It composes Core, ASP.NET hosting, and internal SQLite persistence into a ready-made gateway. Applications embedding AIRouter should depend on the NuGet packages instead of the server project.

Release tags also publish the server container to `ghcr.io/dhhieu113pro/ai-router`.

## License

MIT
