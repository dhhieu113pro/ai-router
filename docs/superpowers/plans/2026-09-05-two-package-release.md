# AIRouter Two-Package Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship exactly two public NuGet packages, `AIRouter.Core` and `AIRouter.AspNetCore`, plus a tag-driven trusted NuGet/GHCR release pipeline.

**Architecture:** Fold the existing OpenAI-compatible provider implementation into the host-agnostic Core package so Core alone supports provider management, routing, chat, Responses, and streaming with in-memory stores. Keep ASP.NET Core as a thin package that depends on Core and maps `/v1`/management endpoints. Keep SQLite and the standalone server internal to the repository; release tags verify, pack and smoke-test both public packages before trusted publishing and multi-arch GHCR publishing.

**Tech Stack:** .NET 10, ASP.NET Core 10, xUnit, NuGet trusted publishing with `NuGet/login`, GitHub Actions OIDC, Docker Buildx, GHCR.

**Spec:** `docs/superpowers/specs/2026-09-05-two-package-release-design.md`

## Global Constraints

- Exactly two public NuGet package IDs: `AIRouter.Core` and `AIRouter.AspNetCore`.
- `AIRouter.Core` must not reference `Microsoft.AspNetCore.App`, ASP.NET endpoint types, EF Core SQLite, SQLite, or `AiRouter.Server`.
- Core alone must support provider management, OpenAI-compatible upstream providers, fallback/round-robin routing, chat, Responses, streaming, health/cooldown, and in-memory provider/route stores.
- `AIRouter.AspNetCore` depends on the matching `AIRouter.Core` package version and contains hosting/HTTP mapping only.
- SQLite remains internal and is not packed.
- `AiRouter.Server` remains an executable/container and is not packed.
- Release tags are `v<semver>` and must point to a commit contained in `main`.
- NuGet publishing uses GitHub OIDC trusted publishing with `environment: production`, `id-token: write`, and pinned `NuGet/login`; no long-lived NuGet API-key repository secret.
- GHCR publishes `linux/amd64` and `linux/arm64` using `GITHUB_TOKEN` with `packages: write`.
- PR CI never authenticates to NuGet or pushes images.
- Final Release build must have zero warnings.

---

### Task 1: Make `AIRouter.Core` the complete non-web library

**Files:**
- Rename/migrate: `src/AiRouter/` -> `src/AIRouter.Core/`
- Move source from: `src/AiRouter.Providers.OpenAI/` -> `src/AIRouter.Core/Providers/OpenAI/` (or equivalent focused folders under Core)
- Modify: `src/AIRouter.Core/AIRouter.Core.csproj`
- Modify: `AiRouter.slnx`
- Modify tests that currently reference `AiRouter` and `AiRouter.Providers.OpenAI`
- Remove: public project `src/AiRouter.Providers.OpenAI/AiRouter.Providers.OpenAI.csproj` after source migration
- Test: `tests/AiRouter.Tests/*`
- Test: existing OpenAI provider tests, migrated to target Core

**Interfaces:**
- Produces package `AIRouter.Core`.
- Produces one DI entry point `AddAIRouter()` that registers Core routing plus the built-in OpenAI-compatible upstream provider/factory.
- Existing `IAiRouter`, `IProviderManager`, `IProviderStore`, `IRouteStore`, provider definitions, route definitions, `ChatAsync`, `ResponsesAsync`, and streaming semantics remain behaviorally compatible.

- [ ] **Step 1: Add/adjust failing architecture and package tests**

Add tests that assert Core is self-sufficient and host-agnostic:

```csharp
[Fact]
public void Core_package_is_the_only_dependency_needed_for_builtin_openai_provider()
{
    var project = File.ReadAllText(RepoPath("src/AIRouter.Core/AIRouter.Core.csproj"));
    Assert.Contains("<PackageId>AIRouter.Core</PackageId>", project);
    Assert.DoesNotContain("Microsoft.AspNetCore.App", project);
    Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Sqlite", project);
}

[Fact]
public void AddAIRouter_registers_builtin_openai_provider_support()
{
    var services = new ServiceCollection();
    services.AddAIRouter();
    using var provider = services.BuildServiceProvider();

    Assert.NotNull(provider.GetRequiredService<IAiRouter>());
    Assert.NotNull(provider.GetRequiredService<IAiProviderFactory>());
}
```

- [ ] **Step 2: Run the targeted tests and verify RED**

Run:

```bash
dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj -c Release
```

Expected: FAIL because the new project/package boundary and combined registration do not exist yet.

- [ ] **Step 3: Migrate Core and provider code**

Create `src/AIRouter.Core/AIRouter.Core.csproj` with explicit package metadata and existing non-web dependencies. Move the existing OpenAI-compatible provider source into Core, preserving namespaces where doing so avoids unnecessary consumer breakage. `AddAIRouter()` must register the provider factory/HTTP client support required for the built-in OpenAI-compatible provider.

Core project metadata must include at least:

```xml
<PropertyGroup>
  <PackageId>AIRouter.Core</PackageId>
  <Title>AIRouter.Core</Title>
  <Description>Host-agnostic AI provider routing for .NET with provider management, fallback, round-robin, Responses and streaming.</Description>
  <RepositoryUrl>https://github.com/dhhieu113pro/ai-router</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <IsPackable>true</IsPackable>
</PropertyGroup>
```

Delete the separate provider project only after all references/tests target Core successfully.

- [ ] **Step 4: Run Core + provider tests and verify GREEN**

Run:

```bash
dotnet restore AiRouter.slnx
dotnet build AiRouter.slnx -c Release --no-restore
dotnet test AiRouter.slnx -c Release --no-build
```

Expected: all existing routing/provider/streaming tests pass; build has zero warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: consolidate AIRouter Core package"
```

---

### Task 2: Make `AIRouter.AspNetCore` the only web package

**Files:**
- Rename/migrate: `src/AiRouter.AspNetCore/` -> `src/AIRouter.AspNetCore/`
- Modify: `src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj`
- Modify endpoint extension files under `src/AIRouter.AspNetCore/`
- Modify: `AiRouter.slnx`
- Modify: `src/AiRouter.Server/AiRouter.Server.csproj`
- Modify: `src/AiRouter.Server/Program.cs`
- Test: `tests/AiRouter.AspNetCore.Tests/*`

**Interfaces:**
- Produces package `AIRouter.AspNetCore` with a package dependency on `AIRouter.Core`.
- `AddAIRouterAspNetCore()` registers only HTTP hosting support.
- `MapAIRouterOpenAI()` (or the existing endpoint mapper retained as a compatibility alias) maps `POST /v1/chat/completions`, `POST /v1/responses`, and `GET /v1/models` and delegates to the Core `IAiRouter`.

- [ ] **Step 1: Add failing public-package boundary tests**

```csharp
[Fact]
public void AspNetCore_package_depends_on_core_and_not_internal_projects()
{
    var project = File.ReadAllText(RepoPath("src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj"));
    Assert.Contains("AIRouter.Core", project);
    Assert.DoesNotContain("Persistence.Sqlite", project);
    Assert.DoesNotContain("AiRouter.Server", project);
}
```

Keep endpoint behavior tests proving the resolved `IAiRouter` instance is the same instance used by the host.

- [ ] **Step 2: Run ASP.NET tests and verify RED**

```bash
dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj -c Release
```

Expected: FAIL until project/package names and references are migrated.

- [ ] **Step 3: Migrate the web project and package metadata**

Set:

```xml
<PropertyGroup>
  <PackageId>AIRouter.AspNetCore</PackageId>
  <Title>AIRouter.AspNetCore</Title>
  <Description>ASP.NET Core OpenAI-compatible hosting adapter for AIRouter.Core.</Description>
  <RepositoryUrl>https://github.com/dhhieu113pro/ai-router</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <IsPackable>true</IsPackable>
</PropertyGroup>
```

Reference only `../AIRouter.Core/AIRouter.Core.csproj` plus `Microsoft.AspNetCore.App`. Update the standalone server to consume Core + AspNetCore + internal SQLite.

- [ ] **Step 4: Run endpoint/server tests and verify GREEN**

```bash
dotnet build AiRouter.slnx -c Release
dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj -c Release --no-build
```

Expected: `/v1` routes, streaming, error mapping, management/auth, and standalone-host tests pass with zero warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: expose AIRouter AspNetCore package"
```

---

### Task 3: Pack and smoke-test exactly two public NuGet packages

**Files:**
- Create: `tests/PackageSmoke/` or `scripts/test-packages.*` using the repository's simplest existing test tooling
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/library-usage.md`
- Modify packaging tests under `tests/AiRouter.Tests/`

**Interfaces:**
- CI output contains only `AIRouter.Core.<version>.nupkg` and `AIRouter.AspNetCore.<version>.nupkg` from public projects.
- SQLite/server projects must have `<IsPackable>false</IsPackable>` or otherwise be excluded from public packaging.

- [ ] **Step 1: Add failing package-contract tests**

Assert documentation mentions only the two public package IDs and no longer instructs users to install `AiRouter.Providers.OpenAI` or SQLite for the normal library path.

Also add a smoke consumer for Core that references the generated local package and compiles code equivalent to:

```csharp
var services = new ServiceCollection();
services.AddAIRouter();
using var provider = services.BuildServiceProvider();
_ = provider.GetRequiredService<IAiRouter>();
_ = provider.GetRequiredService<IProviderManager>();
```

Add an ASP.NET smoke consumer that references only `AIRouter.AspNetCore` (allowing its transitive dependency on Core) and compiles:

```csharp
builder.Services.AddAIRouter().AddAIRouterAspNetCore();
var app = builder.Build();
app.MapAIRouterOpenAI();
```

- [ ] **Step 2: Run package tests and verify RED**

```bash
dotnet pack src/AIRouter.Core/AIRouter.Core.csproj -c Release -o artifacts/packages -p:Version=0.0.0-ci.1
dotnet pack src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj -c Release -o artifacts/packages -p:Version=0.0.0-ci.1
```

Expected: RED until package references/metadata/docs/smoke consumers are correct.

- [ ] **Step 3: Implement package smoke test and normal CI packaging**

Update normal CI to run restore/build/test, determine `0.0.0-ci.${GITHUB_RUN_NUMBER}` for non-tags, pack both public projects with `ContinuousIntegrationBuild=true`, then execute package smoke tests against `artifacts/packages`.

- [ ] **Step 4: Verify package artifacts**

Run the local-equivalent commands and assert exactly two public `.nupkg` package IDs are produced and both smoke consumers restore/build successfully.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "ci: validate AIRouter NuGet packages"
```

---

### Task 4: Add trusted NuGet release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Trigger: push tags `v*` plus optional `workflow_dispatch` only if it cannot publish without a valid tag.
- Outputs package version from tag minus `v`.
- Trusted publishing configuration must match repository `dhhieu113pro/ai-router`, workflow `.github/workflows/release.yml`, environment `production`.

- [ ] **Step 1: Add workflow-lint/contract assertions**

Add repository tests (or a script called by CI) asserting `release.yml` contains:

```yaml
permissions:
  contents: read
```

and tag publish job contains:

```yaml
environment: production
permissions:
  contents: read
  id-token: write
```

with pinned `NuGet/login` and no `${{ secrets.NUGET_API_KEY }}` reference.

- [ ] **Step 2: Verify RED**

Run the workflow-contract test; expected FAIL because `release.yml` does not yet exist.

- [ ] **Step 3: Implement release workflow patterned after `roslyn-mcp`**

Workflow stages:

1. `verify` — restore/build/test.
2. `package` — derive semver, assert tag commit is contained in `origin/main`, pack both packages, smoke-test them, upload one `nuget-<version>` artifact containing both `.nupkg` files.
3. `publish-nuget` — `needs: package`, tag-only, `environment: production`, `id-token: write`, download verified artifact, authenticate with pinned `NuGet/login`, then push both packages with `--skip-duplicate`.

Use tag parser equivalent to:

```bash
version="${GITHUB_REF_NAME#v}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]
```

- [ ] **Step 4: Verify workflow contract GREEN**

Confirm the workflow never performs NuGet authentication for PR/push CI and that package publication reuses uploaded verified artifacts rather than repacking.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml tests scripts
git commit -m "ci: publish AIRouter packages with trusted publishing"
```

---

### Task 5: Add standalone container and GHCR tag publishing

**Files:**
- Create: `Dockerfile`
- Create or modify: `.dockerignore`
- Modify: `.github/workflows/release.yml`
- Modify: `README.md`

**Interfaces:**
- Image: `ghcr.io/dhhieu113pro/ai-router`.
- Platforms: `linux/amd64,linux/arm64`.
- Server listens using ASP.NET defaults/configuration and persists SQLite at `/data/ai-router.db` by default.

- [ ] **Step 1: Add failing container workflow contract**

Assert `Dockerfile` exists and release workflow includes Buildx, GHCR login, `packages: write`, and the expected image name/platforms.

- [ ] **Step 2: Verify RED**

Expected FAIL because the Dockerfile/GHCR job is not complete.

- [ ] **Step 3: Implement minimal multi-stage Dockerfile**

Use .NET 10 SDK/runtime images, restore/publish `src/AiRouter.Server/AiRouter.Server.csproj`, copy only published output to the runtime stage, create/use `/data`, expose the application port, and run `AiRouter.Server.dll`. Do not install Node, Python, CUDA/Vulkan, ffmpeg, or local-LLM tooling.

- [ ] **Step 4: Extend release workflow with GHCR publish**

After verified package/tests succeed, authenticate to `ghcr.io` with `${{ github.actor }}` + `${{ secrets.GITHUB_TOKEN }}` under a job with `packages: write`, then use Buildx to publish `linux/amd64,linux/arm64` tags:

```text
<version>
<major.minor>
latest
```

For `v0.1.0`: `0.1.0`, `0.1`, `latest`.

- [ ] **Step 5: Verify Docker build locally/in CI**

Run:

```bash
docker build -t ai-router:test .
```

If Docker is available in CI, start the container with a writable `/data` mount and assert `/health` returns HTTP 200.

- [ ] **Step 6: Commit**

```bash
git add Dockerfile .dockerignore .github/workflows/release.yml README.md tests scripts
git commit -m "ci: publish AIRouter container to GHCR"
```

---

### Task 6: Final docs, review, and merge-ready verification

**Files:**
- Modify: `README.md`
- Modify: `docs/library-usage.md`
- Modify PR #1 description if needed

**Interfaces:**
- Documentation has two installation choices only: Core, or Core + AspNetCore.
- Release instructions state trusted publisher prerequisites for both package IDs and the `production` environment.

- [ ] **Step 1: Run full verification from a clean checkout**

```bash
dotnet restore AiRouter.slnx
dotnet build AiRouter.slnx -c Release --no-restore
dotnet test AiRouter.slnx -c Release --no-build
dotnet pack src/AIRouter.Core/AIRouter.Core.csproj -c Release --no-build -o artifacts/packages -p:Version=0.0.0-verify
dotnet pack src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj -c Release --no-build -o artifacts/packages -p:Version=0.0.0-verify
```

Run package smoke tests and Docker build. Expected: all tests pass, pack succeeds, smoke consumers build, Docker build succeeds, and Release build reports zero warnings.

- [ ] **Step 2: Use `superpowers:requesting-code-review` and address findings**

Review specifically for package dependency leakage, duplicated routing behavior in ASP.NET, secret handling, trusted publishing permissions, tag validation, and GHCR permissions/tags.

- [ ] **Step 3: Use `superpowers:verification-before-completion`**

Re-run fresh verification after any review fixes. Do not claim merge-ready from an earlier run.

- [ ] **Step 4: Confirm PR #1 current head CI is green**

Require all checks for the final head SHA to complete successfully before marking ready for review. Do not merge.

- [ ] **Step 5: Commit final documentation/review fixes**

```bash
git add -A
git commit -m "docs: finalize AIRouter package release guidance"
```
