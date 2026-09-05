namespace AiRouter.Tests;

public sealed class ProviderContractTests
{
    [Theory]
    [InlineData("AiRouter.Providers.ProviderDefinition")]
    [InlineData("AiRouter.Providers.ProviderHealth")]
    [InlineData("AiRouter.Providers.IAiProvider")]
    [InlineData("AiRouter.Providers.IAiProviderFactory")]
    [InlineData("AiRouter.Providers.IProviderStore")]
    [InlineData("AiRouter.Providers.IProviderManager")]
    [InlineData("AiRouter.Providers.ProviderManager")]
    public void Core_exposes_provider_management_contracts(string typeName)
    {
        var type = typeof(AiRouterMarker).Assembly.GetType(typeName);
        Assert.NotNull(type);
    }
}
