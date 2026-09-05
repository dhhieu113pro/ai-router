using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Tests;

public sealed class ResidualCoverageTests
{
    [Fact]
    public async Task Adding_provider_without_matching_factory_is_rejected()
    {
        var manager = new ProviderManager(new InMemoryProviderStore(), [new FakeFactory()]);
        await manager.InitializeAsync();
        var definition = new ProviderDefinition(
            "unsupported",
            "Unsupported",
            "missing-factory",
            "https://example.test",
            null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AddAsync(definition));

        Assert.Contains("No provider factory", error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeFactory : IAiProviderFactory
    {
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => new FakeProvider(definition);
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            SendChatAsync(model, requestBody, stream, ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectivityResult(true));
    }
}
