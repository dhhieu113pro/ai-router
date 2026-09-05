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

    [Fact]
    public void Release_workflow_publishes_multiarch_server_container_to_ghcr()
    {
        var dockerfile = RepoPath("Dockerfile");
        Assert.True(File.Exists(dockerfile), "Dockerfile must exist.");

        var workflow = File.ReadAllText(RepoPath(".github/workflows/release.yml"));
        var ci = File.ReadAllText(RepoPath(".github/workflows/ci.yml"));

        Assert.Contains("packages: write", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/setup-qemu-action@v3", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/setup-buildx-action@v3", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/login-action@v3", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/build-push-action@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("ghcr.io/dhhieu113pro/ai-router", workflow, StringComparison.Ordinal);
        Assert.Contains("linux/amd64,linux/arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.GITHUB_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("docker build", ci, StringComparison.Ordinal);
        Assert.Contains("/health", ci, StringComparison.Ordinal);
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
