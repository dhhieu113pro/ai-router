using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Tests;

public sealed class ProviderManagerTests
{
    [Fact]
    public async Task Add_updates_store_and_runtime_snapshot()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();

        var added = await manager.AddAsync(Definition("primary", "secret"));

        Assert.Equal("primary", added.Id);
        Assert.Single(manager.Snapshot);
        Assert.Equal("primary", manager.Snapshot[0].Definition.Id);
    }

    [Fact]
    public async Task Add_rejects_duplicate_id_case_insensitively()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", "secret"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AddAsync(Definition("PRIMARY", "other")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData("/bad")]
    public async Task Add_rejects_invalid_ids(string id)
    {
        var manager = CreateManager();
        await manager.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.AddAsync(Definition(id, "secret")));
    }

    [Fact]
    public async Task Update_without_api_key_preserves_existing_secret()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", "secret"));

        var updated = await manager.UpdateAsync("primary", Definition("primary", null) with { Name = "Renamed" });
        var stored = await manager.GetAsync("primary");

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("secret", stored!.ApiKey);
    }

    [Fact]
    public async Task Enable_disable_rebuilds_snapshot()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", "secret"));

        await manager.SetEnabledAsync("primary", false);
        Assert.Empty(manager.Snapshot);

        await manager.SetEnabledAsync("primary", true);
        Assert.Single(manager.Snapshot);
    }

    [Fact]
    public async Task Test_and_models_delegate_to_runtime_provider()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", "secret"));

        var health = await manager.TestAsync("primary");
        var models = await manager.ListModelsAsync("primary");

        Assert.True(health.Success);
        Assert.Equal(["model-a", "model-b"], models);
    }

    [Fact]
    public async Task Delete_removes_provider_and_runtime_instance()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();
        await manager.AddAsync(Definition("primary", "secret"));

        await manager.DeleteAsync("primary");

        Assert.Null(await manager.GetAsync("primary"));
        Assert.Empty(manager.Snapshot);
    }

    private static ProviderManager CreateManager() => new(new InMemoryProviderStore(), [new FakeProviderFactory()]);

    private static ProviderDefinition Definition(string id, string? key) =>
        new(id, id, "fake", "https://example.test", key, Models: ["model-a", "model-b"], DefaultModel: "model-a");

    private sealed class FakeProviderFactory : IAiProviderFactory
    {
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => new FakeProvider(definition);
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) => Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200 });
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["model-a", "model-b"]);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderConnectivityResult(true));
    }
}
