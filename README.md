# AIRouter

[![NuGet AIRouter.Core](https://img.shields.io/nuget/v/AIRouter.Core.svg?label=AIRouter.Core)](https://www.nuget.org/packages/AIRouter.Core)
[![NuGet AIRouter.AspNetCore](https://img.shields.io/nuget/v/AIRouter.AspNetCore.svg?label=AIRouter.AspNetCore)](https://www.nuget.org/packages/AIRouter.AspNetCore)
[![GHCR](https://img.shields.io/badge/GHCR-ai--router-2496ED?logo=github)](https://github.com/dhhieu113pro/ai-router/pkgs/container/ai-router)

AIRouter is a library-first AI provider router for .NET 10. Use it directly from any .NET application, optionally expose an OpenAI-compatible `/v1` API from your own ASP.NET Core host, or run the ready-made `AiRouter.Server` gateway/container with its bundled Angular admin console.

## Packages

There are exactly two public NuGet packages.

### AIRouter.Core

[View `AIRouter.Core` on NuGet.org](https://www.nuget.org/packages/AIRouter.Core)

Use `AIRouter.Core` in console apps, workers, Windows services, desktop apps, MCP servers, or any other .NET project. Core includes provider management, built-in OpenAI-compatible upstream providers, fallback and round-robin routing, Responses support, streaming, health/cooldown behavior, and in-memory provider/route stores.

```bash
dotnet add package AIRouter.Core --version 0.0.1
```

```xml
<PackageReference Include="AIRouter.Core" Version="0.0.1" />
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

[View `AIRouter.AspNetCore` on NuGet.org](https://www.nuget.org/packages/AIRouter.AspNetCore)

Add `AIRouter.AspNetCore` when your application should expose the OpenAI-compatible API. It depends on `AIRouter.Core` and only adds ASP.NET Core hosting integration.

```bash
dotnet add package AIRouter.AspNetCore --version 0.0.1
```

```xml
<PackageReference Include="AIRouter.AspNetCore" Version="0.0.1" />
```

```csharp
builder.Services.AddAiRouter();

var app = builder.Build();
app.UseAiRouter();
```

By default this maps:

- `POST /v1/chat/completions`
- `POST /v1/responses`
- `GET /v1/models`

Use an optional prefix when the routes should live below another path:

```csharp
app.UseAiRouter("api");
```

That maps the same endpoints at `/api/v1/...`. Prefixes such as `api`, `/api`, `api/`, and `/api/` are normalized to the same route prefix.

Management endpoints are optional and can use the same bearer key:

```csharp
app.MapAiRouterManagementEndpoints(adminKey);
app.MapAiRouterConfigurationManagementEndpoints(adminKey);
```

See [docs/library-usage.md](docs/library-usage.md) for provider, route, custom-host, and streaming examples.

## Standalone server and Angular admin

`AiRouter.Server` is optional. It composes Core, ASP.NET hosting, internal SQLite persistence, and a small Angular admin application into one ready-made gateway. Applications embedding AIRouter should depend on the NuGet packages instead of the server project.

The release container is published to [GitHub Container Registry](https://github.com/dhhieu113pro/ai-router/pkgs/container/ai-router):

```bash
docker pull ghcr.io/dhhieu113pro/ai-router:latest
```

Run the latest image with an admin key:

```bash
docker run --rm \
  -p 8080:8080 \
  -e AIROUTER_ADMIN_KEY=change-me \
  -v ai-router-data:/data \
  ghcr.io/dhhieu113pro/ai-router:latest
```

Then open `http://localhost:8080/admin/` and unlock it with the value of `AIROUTER_ADMIN_KEY`.

The admin console provides:

- provider add/edit/delete and enable/disable,
- priority, health, connectivity tests, and model discovery,
- fallback and round-robin route editing,
- JSON configuration import/export,
- responsive system light/dark appearance.

The server also exposes `/health` and the OpenAI-compatible `/v1` routes. By default SQLite is stored at `/data/ai-router.db`.

Optional server keys:

```text
AIROUTER_API_KEY    Protects /v1 routes when configured
AIROUTER_ADMIN_KEY  Enables and protects management/configuration routes when configured
```

Provider and route management remains unavailable when `AIROUTER_ADMIN_KEY` is not configured.

### Configuration migration API

When the admin key is configured, the standalone server maps:

```text
GET  /config/export?includeSecrets=false
POST /config/import?mode=merge
POST /config/import?mode=replace
```

Configuration documents use `schemaVersion: 1`. Export redacts provider API keys unless `includeSecrets=true` is explicitly requested.

`merge` adds or updates matching provider/route ids and preserves unrelated configuration. `replace` additionally deletes provider/route ids that are absent from the imported file. During provider updates, an imported `apiKey: null` preserves the currently stored key.

## Releases

Push a semantic-version tag such as `v0.1.0` from a commit contained in `main`.

The dedicated release workflow then:

1. restores, builds, and tests the solution,
2. packs and smoke-tests `AIRouter.Core` and `AIRouter.AspNetCore`,
3. publishes both packages to NuGet.org through trusted publishing,
4. runs the Angular tests/production build as part of the server container build,
5. publishes `linux/amd64` and `linux/arm64` server images to GHCR as `0.1.0`, `0.1`, and `latest`.

NuGet.org trusted publishers for both package IDs must target repository `dhhieu113pro/ai-router`, workflow `.github/workflows/release.yml`, and GitHub environment `production`. No long-lived NuGet API-key repository secret is required.

## License

MIT
