namespace AiRouter.Tests;

public sealed class PackagingTests
{
    [Fact]
    public void Core_project_has_explicit_nuget_metadata_and_remains_host_agnostic()
    {
        var project = File.ReadAllText(RepoPath("src/AiRouter/AiRouter.csproj"));

        Assert.Contains("<PackageId>AIRouter.Core</PackageId>", project, StringComparison.Ordinal);
        Assert.Contains("<IsPackable>true</IsPackable>", project, StringComparison.Ordinal);
        Assert.Contains("<Description>", project, StringComparison.Ordinal);
        Assert.Contains("<RepositoryUrl>https://github.com/dhhieu113pro/ai-router</RepositoryUrl>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("AspNetCore", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFrameworkCore", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sqlite", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AiRouter.Server", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_core_and_aspnetcore_are_public_packable_packages()
    {
        var core = File.ReadAllText(RepoPath("src/AiRouter/AiRouter.csproj"));
        var aspnet = File.ReadAllText(RepoPath("src/AiRouter.AspNetCore/AiRouter.AspNetCore.csproj"));
        var sqlite = File.ReadAllText(RepoPath("src/AiRouter.Persistence.Sqlite/AiRouter.Persistence.Sqlite.csproj"));
        var server = File.ReadAllText(RepoPath("src/AiRouter.Server/AiRouter.Server.csproj"));
        var solution = File.ReadAllText(RepoPath("AiRouter.slnx"));

        Assert.Contains("<PackageId>AIRouter.Core</PackageId>", core, StringComparison.Ordinal);
        Assert.Contains("<PackageId>AIRouter.AspNetCore</PackageId>", aspnet, StringComparison.Ordinal);
        Assert.Contains("<IsPackable>false</IsPackable>", sqlite, StringComparison.Ordinal);
        Assert.Contains("<IsPackable>false</IsPackable>", server, StringComparison.Ordinal);
        Assert.DoesNotContain("AiRouter.Providers.OpenAI.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_leads_consumers_to_two_public_packages_and_optional_server()
    {
        var path = RepoPath("README.md");
        Assert.True(File.Exists(path), "README.md must exist at the repository root.");
        var readme = File.ReadAllText(path);

        Assert.Contains("AIRouter.Core", readme, StringComparison.Ordinal);
        Assert.Contains("AIRouter.AspNetCore", readme, StringComparison.Ordinal);
        Assert.Contains("library", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenAI-compatible", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/library-usage.md", readme, StringComparison.Ordinal);
        Assert.Contains("AiRouter.Server", readme, StringComparison.Ordinal);
        Assert.Contains("optional", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackageReference Include=\"AiRouter.Providers.OpenAI\"", readme, StringComparison.Ordinal);
    }

    private static string RepoPath(string relative)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
            current = current.Parent;

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
