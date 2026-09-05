namespace AiRouter.Tests;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void Release_workflow_uses_trusted_publishing_for_both_packages()
    {
        var path = RepoPath(".github/workflows/release.yml");
        Assert.True(File.Exists(path), "release.yml must exist.");
        var workflow = File.ReadAllText(path);

        Assert.Contains("tags: [\"v*\"]", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: production", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains("NuGet/login@8d196754b4036150537f80ac539e15c2f1028841", workflow, StringComparison.Ordinal);
        Assert.Contains("user: dhhieu113", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.NUGET_API_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("git merge-base --is-ancestor", workflow, StringComparison.Ordinal);
        Assert.Contains("AIRouter.Core.${PACKAGE_VERSION}.nupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("AIRouter.AspNetCore.${PACKAGE_VERSION}.nupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("--skip-duplicate", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v7", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@v7", workflow, StringComparison.Ordinal);
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
