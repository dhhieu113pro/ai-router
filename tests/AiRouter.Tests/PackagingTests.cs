namespace AiRouter.Tests;

public sealed class PackagingTests
{
    [Fact]
    public void Core_project_has_explicit_nuget_metadata_and_remains_host_agnostic()
    {
        var project = File.ReadAllText(RepoPath("src/AiRouter/AiRouter.csproj"));

        Assert.Contains("<PackageId>AiRouter</PackageId>", project, StringComparison.Ordinal);
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
    public void Readme_leads_consumers_to_library_custom_host_and_openai_host_usage()
    {
        var path = RepoPath("README.md");
        Assert.True(File.Exists(path), "README.md must exist at the repository root.");
        var readme = File.ReadAllText(path);

        Assert.Contains("library", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenAI-compatible", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/library-usage.md", readme, StringComparison.Ordinal);
        Assert.Contains("AiRouter.Server", readme, StringComparison.Ordinal);
        Assert.Contains("optional", readme, StringComparison.OrdinalIgnoreCase);
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
