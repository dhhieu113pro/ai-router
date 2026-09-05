# AIRouter Two-Package Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship exactly two public NuGet packages, `AIRouter.Core` and `AIRouter.AspNetCore`, plus a tag-driven trusted NuGet/GHCR release pipeline.

**Architecture:** Fold the existing OpenAI-compatible provider implementation into the host-agnostic Core package so Core alone supports provider management, routing, chat, Responses, and streaming with in-memory stores. Keep ASP.NET Core as a thin package that depends on Core and maps `/v1`/management endpoints. Keep SQLite and the standalone server internal to the repository; release tags verify, pack and smoke-test both public packages before trusted publishing and multi-arch GHCR publishing.

**Tech Stack:** .NET 10, ASP.NET Core 10, xUnit, NuGet trusted publishing with `NuGet/login`, GitHub Actions OIDC, Docker Buildx, GHCR.

**Spec:** `docs/superpowers/specs/2026-09-05-two-package-release-design.md`

## Global Constraints

- Exactly two public NuGet package IDs: `AIRouter.Core` and `AIRouter.AspNetCore`.
- Preserve existing .NET public API names such as `IAiRouter`, `AddAiRouter()`, `AddAiRouterAspNetCore()`, and `MapAiRouterOpenAiEndpoints()` to avoid unnecessary breaking changes. Branded aliases such as `AddAIRouter()` may delegate to existing APIs, but package renaming must not force API renaming.
- `AIRouter.Core` must not reference `Microsoft.AspNetCore.App`, ASP.NET endpoint types, EF Core SQLite, SQLite, or `AiRouter.Server`.
- Core alone must support provider management, OpenAI-compatible upstream providers, fallback/round-robin routing, chat, Responses, streaming, health/cooldown, and in-memory provider/route stores.
- `AIRouter.AspNetCore` depends on the matching `AIRouter.Core` package version and contains hosting/HTTP mapping only.
- SQLite remains internal and is not packed. `AiRouter.Server` remains executable/container-only and is not packed.
- Release tags are `v<semver>` and must point to a commit contained in `main`.
- NuGet publishing uses GitHub OIDC trusted publishing with `environment: production`, `id-token: write`, and pinned `NuGet/login`; no long-lived NuGet API-key repository secret.
- GHCR publishes `linux/amd64` and `linux/arm64` using `GITHUB_TOKEN` with `packages: write`.
- PR CI never authenticates to NuGet or pushes images.
- Final Release build must have zero warnings.

---

### Task 1: Consolidate Core and built-in OpenAI provider

**Files:**
- Rename/migrate: `src/AiRouter/` -> `src/AIRouter.Core/`
- Move: all source under `src/AiRouter.Providers.OpenAI/` into focused folders under `src/AIRouter.Core/Providers/OpenAI/`
- Modify: `src/AIRouter.Core/AIRouter.Core.csproj`
- Modify: `AiRouter.slnx`
- Modify: tests currently referencing `AiRouter.Providers.OpenAI`
- Remove: `src/AiRouter.Providers.OpenAI/AiRouter.Providers.OpenAI.csproj` after migration

**Interfaces:**
- Produces package `AIRouter.Core`.
- Existing `IAiRouter`, `IProviderManager`, `IProviderStore`, `IRouteStore`, provider/route definitions, `ChatAsync`, `ResponsesAsync`, and stream behavior remain compatible.
- Existing `AddAiRouter()` becomes sufficient to use the built-in OpenAI-compatible provider. If provider registration is still separated internally, `AddAiRouter()` must call that registration before returning.

- [ ] **Step 1: Add failing package/architecture tests**

```csharp
[Fact]
public void Core_package_is_host_agnostic_and_contains_builtin_provider()
{
    var project = File.ReadAllText(RepoPath("src/AIRouter.Core/AIRouter.Core.csproj"));
    Assert.Contains("<PackageId>AIRouter.Core</PackageId>", project);
    Assert.DoesNotContain("Microsoft.AspNetCore.App", project);
    Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Sqlite", project);
}

[Fact]
public void AddAiRouter_registers_router_and_provider_factory()
{
    var services = new ServiceCollection();
    services.AddAiRouter();
    using var provider = services.BuildServiceProvider();
    Assert.NotNull(provider.GetRequiredService<IAiRouter>());
    Assert.NotNull(provider.GetRequiredService<IAiProviderFactory>());
}
```

- [ ] **Step 2: Run targeted tests and verify RED**

```bash
dotnet test tests/AiRouter.Tests/AiRouter.Tests.csproj -c Release
```

Expected: FAIL because the new project/package layout and combined provider registration are not complete.

- [ ] **Step 3: Migrate source and project metadata**

`AIRouter.Core.csproj` must contain:

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

Keep namespaces/public type names compatible where practical; only project/package identity changes are required.

- [ ] **Step 4: Run full build/tests and verify GREEN**

```bash
dotnet restore AiRouter.slnx
dotnet build AiRouter.slnx -c Release --no-restore
dotnet test AiRouter.slnx -c Release --no-build
```

Expected: all routing/provider/streaming tests pass and build reports zero warnings.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: consolidate AIRouter Core package"
```

---

### Task 2: Publish only the ASP.NET hosting adapter as `AIRouter.AspNetCore`

**Files:**
- Rename/migrate: `src/AiRouter.AspNetCore/` -> `src/AIRouter.AspNetCore/`
- Modify: `src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj`
- Modify: `AiRouter.slnx`
- Modify: `src/AiRouter.Server/AiRouter.Server.csproj`
- Modify: `src/AiRouter.Server/Program.cs`
- Test: `tests/AiRouter.AspNetCore.Tests/*`

**Interfaces:**
- Produces package `AIRouter.AspNetCore`.
- Retains `AddAiRouterAspNetCore()` and `MapAiRouterOpenAiEndpoints()` behavior; optional branded aliases may delegate to them.
- Maps `POST /v1/chat/completions`, `POST /v1/responses`, and `GET /v1/models` using the same `IAiRouter` supplied by Core.

- [ ] **Step 1: Add failing boundary test**

```csharp
[Fact]
public void AspNetCore_package_only_depends_on_core_plus_aspnet()
{
    var project = File.ReadAllText(RepoPath("src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj"));
    Assert.Contains("AIRouter.Core", project);
    Assert.DoesNotContain("Persistence.Sqlite", project);
    Assert.DoesNotContain("AiRouter.Server", project);
}
```

- [ ] **Step 2: Run ASP.NET tests and verify RED**

```bash
dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj -c Release
```

- [ ] **Step 3: Migrate package metadata and references**

Use:

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

Reference `../AIRouter.Core/AIRouter.Core.csproj` plus `Microsoft.AspNetCore.App`. The server references Core + AspNetCore + internal SQLite.

- [ ] **Step 4: Verify endpoint/server tests GREEN**

```bash
dotnet build AiRouter.slnx -c Release
dotnet test tests/AiRouter.AspNetCore.Tests/AiRouter.AspNetCore.Tests.csproj -c Release --no-build
```

Expected: `/v1`, SSE, error mapping, management/auth, and server-host tests pass with zero warnings.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: expose AIRouter AspNetCore package"
```

---

### Task 3: Pack and smoke-test exactly two public packages in normal CI

**Files:**
- Modify: `.github/workflows/ci.yml`
- Create: `scripts/test-packages.py` (or equivalent single-purpose smoke-test script)
- Modify: `README.md`
- Modify: `docs/library-usage.md`
- Modify packaging tests under `tests/AiRouter.Tests/`
- Modify: `src/AiRouter.Persistence.Sqlite/AiRouter.Persistence.Sqlite.csproj`
- Modify: `src/AiRouter.Server/AiRouter.Server.csproj`

**Interfaces:**
- Public artifacts: `AIRouter.Core.<version>.nupkg`, `AIRouter.AspNetCore.<version>.nupkg` only.
- SQLite/server set `<IsPackable>false</IsPackable>`.

- [ ] **Step 1: Add failing docs/package tests**

Assert README/usage docs contain `AIRouter.Core` and `AIRouter.AspNetCore`, and no longer instruct primary consumers to install `AiRouter.Providers.OpenAI` or SQLite.

- [ ] **Step 2: Verify RED by packing**

```bash
dotnet pack src/AIRouter.Core/AIRouter.Core.csproj -c Release -o artifacts/packages -p:Version=0.0.0-ci.1
dotnet pack src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj -c Release -o artifacts/packages -p:Version=0.0.0-ci.1
```

- [ ] **Step 3: Add package smoke consumers**

Core smoke source must compile after referencing only `AIRouter.Core`:

```csharp
var services = new ServiceCollection();
services.AddAiRouter();
using var sp = services.BuildServiceProvider();
_ = sp.GetRequiredService<IAiRouter>();
_ = sp.GetRequiredService<IProviderManager>();
```

ASP.NET smoke source references `AIRouter.AspNetCore` and compiles:

```csharp
builder.Services.AddAiRouter().AddAiRouterAspNetCore();
var app = builder.Build();
app.MapAiRouterOpenAiEndpoints();
```

- [ ] **Step 4: Update normal CI**

After restore/build/test, derive `0.0.0-ci.${GITHUB_RUN_NUMBER}`, pack both projects with `-p:ContinuousIntegrationBuild=true`, then run `scripts/test-packages.py artifacts/packages <version>`.

- [ ] **Step 5: Verify GREEN and commit**

Expected: exactly two public package IDs, both smoke consumers restore/build, zero warnings.

```bash
git add -A && git commit -m "ci: validate AIRouter NuGet packages"
```

---

### Task 4: Add dedicated trusted NuGet release workflow

**Files:**
- Create: `.github/workflows/release.yml`
- Add workflow-contract tests/scripts used by CI

**Interfaces:**
- Trigger: push tags `v*`.
- Trusted publisher: repo `dhhieu113pro/ai-router`, workflow `.github/workflows/release.yml`, environment `production`.
- Publishes already-verified package artifacts; never repacks in publish job.

- [ ] **Step 1: Add failing workflow contract**

Assert `release.yml` requires tag semver, contains `environment: production`, `id-token: write`, pinned `NuGet/login`, and contains no `${{ secrets.NUGET_API_KEY }}`.

- [ ] **Step 2: Verify RED**

Expected: FAIL because `release.yml` does not exist.

- [ ] **Step 3: Implement `verify`, `package`, and `publish-nuget` jobs**

Tag version logic:

```bash
version="${GITHUB_REF_NAME#v}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  echo "Invalid package version: $version" >&2
  exit 1
fi
```

`package` must run `git merge-base --is-ancestor "$GITHUB_SHA" origin/main`, pack/smoke-test both packages and upload one artifact. `publish-nuget` downloads that artifact, authenticates with pinned `NuGet/login`, and pushes both `.nupkg` files with `--skip-duplicate`.

- [ ] **Step 4: Verify workflow contract GREEN and commit**

```bash
git add .github/workflows/release.yml scripts tests && git commit -m "ci: publish AIRouter packages with trusted publishing"
```

---

### Task 5: Add standalone container and GHCR publishing

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Modify: `.github/workflows/release.yml`
- Modify: README container section
- Extend workflow-contract tests

**Interfaces:**
- Image `ghcr.io/dhhieu113pro/ai-router`.
- Platforms `linux/amd64,linux/arm64`.
- Tags for `v0.1.0`: `0.1.0`, `0.1`, `latest`.

- [ ] **Step 1: Add failing Docker/GHCR contract**

Assert Dockerfile exists and release workflow has Buildx, GHCR login, `packages: write`, expected image name and both platforms.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Implement minimal .NET 10 multi-stage Dockerfile**

Build/publish `src/AiRouter.Server/AiRouter.Server.csproj`, copy published output into ASP.NET runtime image, ensure `/data` exists, expose the app port, and run `AiRouter.Server.dll`. Do not install Node, Python, CUDA/Vulkan, ffmpeg, or local-LLM tooling.

- [ ] **Step 4: Add GHCR tag publish job**

Use `docker/setup-buildx-action`, `docker/login-action` with `${{ github.actor }}` / `${{ secrets.GITHUB_TOKEN }}`, and `docker/build-push-action` with `platforms: linux/amd64,linux/arm64`. Job permissions include `contents: read` and `packages: write`.

- [ ] **Step 5: Verify Docker build/health and commit**

```bash
docker build -t ai-router:test .
```

If Docker is available in CI, run the container with writable `/data` and assert `/health` returns HTTP 200.

```bash
git add Dockerfile .dockerignore .github/workflows/release.yml README.md scripts tests && git commit -m "ci: publish AIRouter container to GHCR"
```

---

### Task 6: Final documentation, review, and merge-ready verification

**Files:**
- Modify: `README.md`
- Modify: `docs/library-usage.md`
- Update PR #1 description if needed

- [ ] **Step 1: Run full fresh verification**

```bash
dotnet restore AiRouter.slnx
dotnet build AiRouter.slnx -c Release --no-restore
dotnet test AiRouter.slnx -c Release --no-build
dotnet pack src/AIRouter.Core/AIRouter.Core.csproj -c Release --no-build -o artifacts/packages -p:Version=0.0.0-verify
dotnet pack src/AIRouter.AspNetCore/AIRouter.AspNetCore.csproj -c Release --no-build -o artifacts/packages -p:Version=0.0.0-verify
python scripts/test-packages.py artifacts/packages 0.0.0-verify
docker build -t ai-router:test .
```

Expected: all tests pass, both packages smoke-test, Docker builds, Release build reports zero warnings.

- [ ] **Step 2: Use `superpowers:requesting-code-review` and address findings**

Review package dependency leakage, public API compatibility, duplicated routing behavior, secret handling, trusted-publishing permissions, tag validation, and GHCR tags/permissions.

- [ ] **Step 3: Use `superpowers:verification-before-completion` and rerun fresh verification after fixes**

- [ ] **Step 4: Confirm PR #1 current-head GitHub Actions checks are all green, then mark ready for review; do not merge.**
