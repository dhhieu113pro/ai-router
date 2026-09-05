#!/usr/bin/env bash
set -euo pipefail

packages_dir="${1:?usage: test-packages.sh <packages-dir> <version>}"
version="${2:?usage: test-packages.sh <packages-dir> <version>}"
packages_dir="$(realpath "$packages_dir")"

core_package="$packages_dir/AIRouter.Core.$version.nupkg"
aspnet_package="$packages_dir/AIRouter.AspNetCore.$version.nupkg"

[[ -f "$core_package" ]] || { echo "Missing $core_package" >&2; exit 1; }
[[ -f "$aspnet_package" ]] || { echo "Missing $aspnet_package" >&2; exit 1; }

mapfile -t public_packages < <(find "$packages_dir" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' | sort)
if [[ ${#public_packages[@]} -ne 2 ]]; then
  printf 'Expected exactly two public packages, found %s:\n' "${#public_packages[@]}" >&2
  printf '  %s\n' "${public_packages[@]}" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat > "$work/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

mkdir -p "$work/core"
cat > "$work/core/CoreSmoke.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AIRouter.Core" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat > "$work/core/Program.cs" <<'EOF'
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAiRouter();
_ = services.Count;
_ = typeof(IAiRouter);
_ = typeof(IProviderManager);
EOF

dotnet restore "$work/core/CoreSmoke.csproj" --configfile "$work/NuGet.Config"
dotnet build "$work/core/CoreSmoke.csproj" --configuration Release --no-restore

mkdir -p "$work/aspnet"
cat > "$work/aspnet/AspNetSmoke.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AIRouter.AspNetCore" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat > "$work/aspnet/Program.cs" <<'EOF'
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAiRouter().AddAiRouterAspNetCore();
var app = builder.Build();
app.MapAiRouterOpenAiEndpoints();
EOF

dotnet restore "$work/aspnet/AspNetSmoke.csproj" --configfile "$work/NuGet.Config"
dotnet build "$work/aspnet/AspNetSmoke.csproj" --configuration Release --no-restore

echo "Verified AIRouter.Core and AIRouter.AspNetCore $version from local NuGet artifacts."
