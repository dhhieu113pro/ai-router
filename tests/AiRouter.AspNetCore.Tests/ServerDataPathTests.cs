namespace AiRouter.AspNetCore.Tests;

public sealed class ServerDataPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_uses_default_for_missing_configuration(string? configured)
    {
        Assert.Equal(Path.GetFullPath("/data/ai-router.db"), ServerDataPath.Resolve(configured));
    }

    [Fact]
    public void Resolve_normalizes_configured_path()
    {
        var configured = Path.Combine(Path.GetTempPath(), "ai-router", "custom.db");
        Assert.Equal(Path.GetFullPath(configured), ServerDataPath.Resolve(configured));
    }
}
