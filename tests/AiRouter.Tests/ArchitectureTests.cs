namespace AiRouter.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Core_project_exists_and_has_no_web_persistence_or_ai_studio_dependencies()
    {
        var path = RepoPath("src/AiRouter/AiRouter.csproj");
        Assert.True(File.Exists(path), $"Missing core project: {path}");

        var xml = File.ReadAllText(path);
        Assert.DoesNotContain("Microsoft.AspNetCore", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFrameworkCore", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sqlite", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AIStudio", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Server_references_only_router_adapters()
    {
        var path = RepoPath("src/AiRouter.Server/AiRouter.Server.csproj");
        Assert.True(File.Exists(path), $"Missing server project: {path}");

        var xml = File.ReadAllText(path);
        Assert.Contains("AiRouter.AspNetCore", xml, StringComparison.Ordinal);
        Assert.Contains("AiRouter.Persistence.Sqlite", xml, StringComparison.Ordinal);
        Assert.Contains("AiRouter.Providers.OpenAI", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("AIStudio", xml, StringComparison.OrdinalIgnoreCase);
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
