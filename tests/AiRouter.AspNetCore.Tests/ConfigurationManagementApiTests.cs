using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class ConfigurationManagementApiTests
{
    [Fact]
    public void Map_configuration_endpoints_rejects_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AiRouterConfigurationManagementEndpointRouteBuilderExtensions.MapAiRouterConfigurationManagementEndpoints(null!));
    }

    [Fact]
    public async Task Configuration_endpoints_require_the_management_bearer_key()
    {
        await using var app = await StartAsync("admin-secret");
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/config/export")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/config/import", new { schemaVersion = 1, providers = Array.Empty<object>(), routes = Array.Empty<object>() })).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/config/export")).StatusCode);
    }

    [Fact]
    public async Task Export_redacts_secrets_by_default_and_can_explicitly_include_them()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("primary", "secret"));
        await client.PostAsJsonAsync("/routes", RouteBody("coding", "primary"));

        using (var response = await client.GetAsync("/config/export"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, body.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("providers")[0].GetProperty("apiKey").ValueKind);
            Assert.Equal("coding", body.RootElement.GetProperty("routes")[0].GetProperty("id").GetString());
        }

        using (var response = await client.GetAsync("/config/export?includeSecrets=true"))
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("secret", body.RootElement.GetProperty("providers")[0].GetProperty("apiKey").GetString());
        }
    }

    [Fact]
    public async Task Merge_import_updates_and_adds_without_erasing_an_existing_secret()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("primary", "keep-me"));

        var response = await client.PostAsJsonAsync("/config/import", new
        {
            schemaVersion = 1,
            providers = new[]
            {
                ProviderBody("primary", null, "Updated"),
                ProviderBody("secondary", "second-secret", "Secondary")
            },
            routes = new[] { RouteBody("coding", "primary") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("merge", body.RootElement.GetProperty("mode").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("providersUpserted").GetInt32());
            Assert.Equal(1, body.RootElement.GetProperty("routesUpserted").GetInt32());
            Assert.Equal(0, body.RootElement.GetProperty("providersDeleted").GetInt32());
            Assert.Equal(0, body.RootElement.GetProperty("routesDeleted").GetInt32());
        }

        var manager = app.Services.GetRequiredService<IProviderManager>();
        Assert.Equal("keep-me", (await manager.GetAsync("primary"))!.ApiKey);
        Assert.Equal("Updated", (await manager.GetAsync("primary"))!.Name);
        Assert.Equal("second-secret", (await manager.GetAsync("secondary"))!.ApiKey);
        Assert.NotNull(await app.Services.GetRequiredService<IRouteStore>().GetAsync("coding"));
    }

    [Fact]
    public async Task Replace_import_deletes_configuration_not_present_in_the_document()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        await client.PostAsJsonAsync("/providers", ProviderBody("keep", "key"));
        await client.PostAsJsonAsync("/providers", ProviderBody("remove", "key"));
        await client.PostAsJsonAsync("/routes", RouteBody("keep-route", "keep"));
        await client.PostAsJsonAsync("/routes", RouteBody("remove-route", "remove"));

        var response = await client.PostAsJsonAsync("/config/import?mode=replace", new
        {
            schemaVersion = 1,
            providers = new[] { ProviderBody("keep", null, "Kept") },
            routes = new[] { RouteBody("keep-route", "keep") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("replace", body.RootElement.GetProperty("mode").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("providersDeleted").GetInt32());
            Assert.Equal(1, body.RootElement.GetProperty("routesDeleted").GetInt32());
        }

        var manager = app.Services.GetRequiredService<IProviderManager>();
        var routes = app.Services.GetRequiredService<IRouteStore>();
        Assert.NotNull(await manager.GetAsync("keep"));
        Assert.Null(await manager.GetAsync("remove"));
        Assert.NotNull(await routes.GetAsync("keep-route"));
        Assert.Null(await routes.GetAsync("remove-route"));
    }

    [Fact]
    public async Task Import_handles_empty_collections_and_validation_errors()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/config/import", new { schemaVersion = 1, providers = (object?)null, routes = (object?)null })).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/config/import", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/config/import", new StringContent("null", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/config/import", new { schemaVersion = 2, providers = Array.Empty<object>(), routes = Array.Empty<object>() })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/config/import?mode=overwrite", new { schemaVersion = 1, providers = Array.Empty<object>(), routes = Array.Empty<object>() })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/config/import", new { schemaVersion = 1, providers = new[] { ProviderBody("", null) }, routes = Array.Empty<object>() })).StatusCode);
    }

    private static object ProviderBody(string id, string? apiKey, string name = "Primary") => new
    {
        id,
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

    private static object RouteBody(string id, string providerId) => new
    {
        id,
        strategy = 0,
        targets = new[] { new { providerId, model = "model-a", priority = 10, enabled = true } },
        enabled = true
    };

    private static async Task<WebApplication> StartAsync(string? adminKey = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAiProviderFactory, FakeProviderFactory>();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();

        var app = builder.Build();
        app.MapAiRouterManagementEndpoints(adminKey);
        app.MapAiRouterConfigurationManagementEndpoints(adminKey);
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
