using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class ManagementApiTests
{
    [Fact]
    public async Task Provider_crud_redacts_secret_and_preserves_it_when_update_omits_key()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var created = await client.PostAsJsonAsync("/providers", ProviderBody("secret"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("apiKey").ValueKind);

        var updated = await client.PutAsJsonAsync("/providers/primary", ProviderBody(null, "Updated"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var manager = app.Services.GetRequiredService<IProviderManager>();
        var stored = await manager.GetAsync("primary");
        Assert.Equal("secret", stored!.ApiKey);
        Assert.Equal("Updated", stored.Name);

        var fetched = await client.GetAsync("/providers/primary");
        var fetchedText = await fetched.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", fetchedText, StringComparison.Ordinal);

        var deleted = await client.DeleteAsync("/providers/primary");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Null(await manager.GetAsync("primary"));
    }

    [Fact]
    public async Task Provider_models_and_health_are_available()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("secret"));

        var models = await client.GetFromJsonAsync<string[]>("/providers/primary/models");
        Assert.Equal(["model-a"], models);

        var health = await client.GetAsync("/providers/primary/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var text = await health.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Route_crud_round_trips()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var created = await client.PostAsJsonAsync("/routes", new
        {
            id = "coding",
            strategy = 0,
            targets = new[] { new { providerId = "primary", model = "model-a", priority = 10, enabled = true } },
            enabled = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var fetched = await client.GetAsync("/routes/coding");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var updated = await client.PutAsJsonAsync("/routes/coding", new
        {
            id = "coding",
            strategy = 1,
            targets = new[] { new { providerId = "primary", model = "model-a", priority = 5, enabled = true } },
            enabled = true
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var route = await app.Services.GetRequiredService<IRouteStore>().GetAsync("coding");
        Assert.Equal(RoutingStrategy.RoundRobin, route!.Strategy);
        Assert.Equal(5, route.Targets[0].Priority);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/routes/coding")).StatusCode);
        Assert.Null(await app.Services.GetRequiredService<IRouteStore>().GetAsync("coding"));
    }

    [Fact]
    public async Task Management_bearer_key_rejects_missing_key_and_accepts_correct_key()
    {
        await using var app = await StartAsync(adminKey: "admin-secret");
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/providers")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/providers")).StatusCode);
    }

    [Fact]
    public async Task OpenAi_bearer_key_protects_v1_endpoints_when_configured()
    {
        await using var app = await StartAsync(apiKey: "api-secret");
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/models")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "api-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/models")).StatusCode);
    }

    private static object ProviderBody(string? apiKey, string name = "Primary") => new
    {
        id = "primary",
        name,
        type = "fake",
        baseUrl = "https://unused.test",
        apiKey,
        enabled = true,
        priority = 10,
        models = new[] { "model-a" },
        defaultModel = "model-a",
        discoverModels = false,
        supportsNativeResponses = true
    };

    private static async Task<WebApplication> StartAsync(string? adminKey = null, string? apiKey = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAiProviderFactory, FakeProviderFactory>();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();

        var app = builder.Build();
        app.MapAiRouterOpenAiEndpoints(apiKey);
        app.MapAiRouterManagementEndpoints(adminKey);
        await app.StartAsync();
        return app;
    }

    private sealed class FakeProviderFactory : IAiProviderFactory
    {
        public bool CanCreate(ProviderDefinition definition) => definition.Type == "fake";
        public IAiProvider Create(ProviderDefinition definition) => new FakeProvider(definition);
    }

    private sealed class FakeProvider(ProviderDefinition definition) : IAiProvider
    {
        public ProviderDefinition Definition { get; } = definition;
        public ProviderHealth Health { get; } = new();
        public Task<ProviderResponse> SendChatAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            Task.FromResult(new ProviderResponse { Success = true, StatusCode = 200, Body = JsonSerializer.SerializeToElement(new { ok = true }) });
        public Task<ProviderResponse> SendResponsesAsync(string model, JsonElement requestBody, bool stream, CancellationToken ct = default) =>
            SendChatAsync(model, requestBody, stream, ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Definition.Models ?? []);
        public Task<ProviderConnectivityResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectivityResult(true));
    }
}
