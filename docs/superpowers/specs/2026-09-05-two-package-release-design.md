# AIRouter Two-Package and Release Design

**Date:** 2026-09-05

## Status

Approved architecture amendment for PR #1. This document supersedes the earlier public-package split where OpenAI provider support and persistence were described as separately consumable packages.

## Goals

AIRouter exposes exactly two public NuGet packages:

1. `AIRouter.Core` — usable from any .NET 10 application without ASP.NET Core or SQLite.
2. `AIRouter.AspNetCore` — optional ASP.NET Core hosting adapter that depends on `AIRouter.Core` and maps OpenAI-compatible `/v1` routes.

The standalone server remains a ready-to-run executable/container and is not a third public NuGet package.

A `v*` release tag publishes both NuGet packages through NuGet trusted publishing and publishes the standalone container to GHCR.

## Public Package Boundary

### `AIRouter.Core`

`AIRouter.Core` is the main product library. Installing only this package must be enough to:

- register AIRouter in dependency injection,
- create, update, enable, disable, test, and remove providers,
- use OpenAI-compatible upstream providers,
- discover/list provider models,
- define fallback and round-robin routes,
- use provider health/cooldown behavior,
- send chat-completion requests,
- send Responses-style requests,
- consume streaming responses,
- use in-memory provider and route storage by default,
- replace storage abstractions with application-owned implementations.

`AIRouter.Core` must not reference:

- `Microsoft.AspNetCore.App`,
- ASP.NET Core endpoint types,
- SQLite/EF Core SQLite,
- `AIRouter.Server`.

The existing OpenAI-compatible provider implementation currently in `AiRouter.Providers.OpenAI` is folded into `AIRouter.Core`; consumers must not need a third provider package for the primary use case.

The public core DI surface remains simple, for example:

```csharp
services.AddAIRouter();
```

A consumer can then resolve `IProviderManager`, `IRouteStore`, and `IAIRouter`, configure providers/routes in memory, and call chat/streaming APIs directly.

### `AIRouter.AspNetCore`

`AIRouter.AspNetCore` depends on `AIRouter.Core` and adds only ASP.NET Core hosting integration.

It owns:

- ASP.NET Core service registration helpers,
- OpenAI-compatible inbound endpoint mapping,
- HTTP request/response translation,
- SSE passthrough for streaming,
- optional API/admin bearer protection helpers,
- optional provider/route management endpoint mapping.

Primary hosted routes:

```text
POST /v1/chat/completions
POST /v1/responses
GET  /v1/models
```

Example target usage:

```csharp
builder.Services
    .AddAIRouter()
    .AddAIRouterAspNetCore();

var app = builder.Build();
app.MapAIRouterOpenAI();
```

`AIRouter.AspNetCore` must not contain routing policy or provider-selection logic; it resolves and delegates to the same `IAIRouter` registered by Core.

## Internal Components

The repository may keep internal projects that support the ready-made server, but they are not public NuGet packages.

### SQLite persistence

SQLite remains optional server infrastructure. It may stay in an internal `AiRouter.Persistence.Sqlite` project for separation and testing, but the project is not published to NuGet.

Core defaults to in-memory stores. Applications that need persistence can implement `IProviderStore` and `IRouteStore` against their own database/configuration system.

### Standalone server

`AiRouter.Server` consumes `AIRouter.Core`, `AIRouter.AspNetCore`, and the internal SQLite implementation. It remains the reference gateway/container and is not packable as a public NuGet library.

## Project Migration

Implementation should converge on the following public project/package model:

```text
src/
  AIRouter.Core/                -> NuGet: AIRouter.Core
  AIRouter.AspNetCore/          -> NuGet: AIRouter.AspNetCore
  AiRouter.Persistence.Sqlite/  -> internal, not packable
  AiRouter.Server/              -> executable/container, not packable
```

The existing `AiRouter.Providers.OpenAI` source is moved into `AIRouter.Core`, then the separate provider project is removed from the public dependency graph.

Assembly/root namespace casing may follow normal .NET conventions, but package IDs are exactly:

- `AIRouter.Core`
- `AIRouter.AspNetCore`

## NuGet Versioning

Release tags use `v<semver>`, for example `v0.1.0`.

The package version is the tag without the leading `v`:

```text
v0.1.0 -> AIRouter.Core 0.1.0
       -> AIRouter.AspNetCore 0.1.0
```

Both packages are always built from the same commit and released at the same version.

`AIRouter.AspNetCore` declares a dependency on the same release version of `AIRouter.Core` so the two public packages stay version-aligned.

## NuGet Trusted Publishing

Publishing follows the existing `dhhieu113pro/roslyn-mcp` pattern and does not use a long-lived `NUGET_API_KEY` repository secret.

The dedicated release workflow is:

```text
.github/workflows/release.yml
```

The tag-only NuGet publish job uses:

- `environment: production`,
- `permissions: id-token: write`,
- `NuGet/login` pinned to an immutable commit,
- NuGet.org trusted publishing to obtain the temporary push credential.

NuGet.org must have trusted-publisher entries for both `AIRouter.Core` and `AIRouter.AspNetCore` pointing to:

- repository: `dhhieu113pro/ai-router`,
- workflow: `.github/workflows/release.yml`,
- environment: `production`.

The workflow packages and validates artifacts before authentication/publish. The publish job downloads the already-verified `.nupkg` artifacts rather than repacking them.

## CI and Release Workflows

### Normal CI — `.github/workflows/ci.yml`

PRs and normal branch pushes run:

1. restore,
2. Release build,
3. tests,
4. pack `AIRouter.Core`,
5. pack `AIRouter.AspNetCore`,
6. package smoke tests that consume the generated `.nupkg` files.

This prevents release-only package failures.

### Release — `.github/workflows/release.yml`

The release workflow triggers only for pushed `v*` tags.

It first verifies that the tag points to a commit contained in `main`, derives and validates the semantic package version, and then runs the same build/test/pack/smoke checks as normal CI.

Only after verification succeeds does it:

1. authenticate to NuGet.org through trusted publishing,
2. push `AIRouter.Core`,
3. push `AIRouter.AspNetCore`,
4. build the standalone server container,
5. publish the multi-architecture image to GHCR.

The NuGet pushes use `--skip-duplicate` so a failed partial release is safely resumable. If Core is accepted by NuGet.org but the AspNetCore push fails, the release is marked failed and GHCR publishing does not run. Re-running the workflow skips the already-published Core version and publishes the missing AspNetCore package before continuing to GHCR.

## GHCR Container Publishing

The standalone server is published as:

```text
ghcr.io/dhhieu113pro/ai-router:<major.minor.patch>
ghcr.io/dhhieu113pro/ai-router:<major.minor>
ghcr.io/dhhieu113pro/ai-router:latest
```

For `v0.1.0` this means:

```text
ghcr.io/dhhieu113pro/ai-router:0.1.0
ghcr.io/dhhieu113pro/ai-router:0.1
ghcr.io/dhhieu113pro/ai-router:latest
```

The image targets:

- `linux/amd64`,
- `linux/arm64`.

GHCR publishing uses `GITHUB_TOKEN` with `packages: write`; no separate registry password is required.

The Docker image contains only the .NET server runtime/published application and does not add Node.js, Python, local-LLM runtimes, CUDA/Vulkan, ffmpeg, or other AI Studio dependencies.

## Failure and Safety Rules

- A failed test/package smoke test blocks both NuGet and GHCR publishing.
- A failed NuGet push blocks GHCR publishing.
- A partially published NuGet release is recovered by rerunning the same tag workflow with `--skip-duplicate`; the workflow never repacks different bits for the same version.
- Tag parsing rejects invalid semantic versions.
- Release tags must point to commits contained in `main`.
- Normal PR builds never authenticate to NuGet or push GHCR images.
- NuGet trusted publishing credentials are short-lived and generated only in the tag publish job.
- Provider API keys remain runtime configuration/data and are never baked into NuGet packages or container layers.

## Documentation

The root README and `docs/library-usage.md` must present only the two public package choices:

```xml
<PackageReference Include="AIRouter.Core" Version="..." />
```

and, for ASP.NET Core hosts:

```xml
<PackageReference Include="AIRouter.Core" Version="..." />
<PackageReference Include="AIRouter.AspNetCore" Version="..." />
```

The docs must not instruct users to install `AiRouter.Providers.OpenAI` or SQLite for the primary library use case.

## Acceptance Criteria

The architecture is complete when:

- exactly `AIRouter.Core` and `AIRouter.AspNetCore` are public packable NuGet projects,
- installing Core alone supports provider management, routing, chat, Responses, and streaming using in-memory storage,
- AspNetCore depends on Core and maps the `/v1` API without duplicating routing behavior,
- both generated packages pass consumer smoke tests,
- a valid `v*` tag can publish both packages using NuGet trusted publishing,
- the same successful tag release can publish the multi-arch standalone server image to GHCR,
- partial NuGet publishing is safely resumable without changing package contents,
- PR CI requires no publishing credentials,
- build/test/pack verification is green with zero build warnings before PR #1 is marked ready for review.
