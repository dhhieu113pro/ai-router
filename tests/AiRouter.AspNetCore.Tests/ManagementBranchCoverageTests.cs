using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class ManagementBranchCoverageTests
{
    [Fact]
    public async Task Every_management_route_rejects_missing_admin_bearer()
    {
        await using var app = await StartManagementAsync(new EmptyManager(), new InMemoryRouteStore(), "secret");
        var client = app.GetTestClient();
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/providers"),
            new HttpRequestMessage(HttpMethod.Post, "/providers"),
            new HttpRequestMessage(HttpMethod.Get, "/providers/primary"),
            new HttpRequestMessage(HttpMethod.Put, "/providers/primary"),
            new HttpRequestMessage(HttpMethod.Delete, "/providers/primary"),
            new HttpRequestMessage(HttpMethod.Post, "/providers/primary/enable"),
            new HttpRequestMessage(HttpMethod.Post, "/providers/primary/disable"),
            new HttpRequestMessage(HttpMethod.Post, "/providers/primary/test"),
            new HttpRequestMessage(HttpMethod.Get, "/providers/primary/models"),
            new HttpRequestMessage(HttpMethod.Get, "/providers/primary/health"),
            new HttpRequestMessage(HttpMethod.Get, "/routes"),
            new HttpRequestMessage(HttpMethod.Post, "/routes"),
            new HttpRequestMessage(HttpMethod.Get, "/routes/route"),
            new HttpRequestMessage(HttpMethod.Put, "/routes/route"),
            new HttpRequestMessage(HttpMethod.Delete, "/routes/route")
        };

        foreach (var request in requests)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Every_OpenAI_route_rejects_missing_api_bearer()
    {
        await using var app = await StartOpenAiAsync("secret");
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/chat/completions", new { model = "m" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/responses", new { model = "m" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/models")).StatusCode);
    }

    [Fact]
    public async Task Enabled_provider_without_runtime_instance_reports_unknown_health()
    {
        var definition = new ProviderDefinition("primary", "Primary", "fake", "https://unused.test", null, Enabled: true);
        await using var app = await StartManagementAsync(new DefinitionOnlyManager(definition), new InMemoryRouteStore());
        var health = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/providers/primary/health");

        Assert.Equal("Unknown", health.GetProperty("status").GetString());
        Assert.Equal(0, health.GetProperty("consecutiveFailures").GetInt32());
    }

    [Fact]
    public async Task Existing_route_can_be_updated_successfully()
    {
        var routes = new InMemoryRouteStore();
        await routes.UpsertAsync(new RouteDefinition("route", RoutingStrategy.Fallback,
            [new RouteTarget("primary", "old")]));
        await using var app = await StartManagementAsync(new EmptyManager(), routes);

        var response = await app.GetTestClient().PutAsJsonAsync("/routes/route", new
        {
            id = "route",
            strategy = 1,
            targets = new[] { new { providerId = "primary", model = "new", priority = 10, enabled = true } },
            enabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new", (await routes.GetAsync("route"))!.Targets.Single().Model);
    }

    private static async Task<WebApplication> StartManagementAsync(IProviderManager manager, IRouteStore routes, string? key = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(manager);
        builder.Services.AddSingleton(routes);
        builder.Services.AddAiRouterAspNetCore();
        var app = builder.Build();
        app.MapAiRouterManagementEndpoints(key);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartOpenAiAsync(string key)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAiRouter>(new Router());
        builder.Services.AddSingleton<IProviderManager>(new EmptyManager());
        builder.Services.AddSingleton<IRouteStore>(new InMemoryRouteStore());
        builder.Services.AddAiRouterAspNetCore();
        var app = builder.Build();
        app.MapAiRouterOpenAiEndpoints(key);
        await app.StartAsync();
        return app;
    }

    private class EmptyManager : IProviderManager
    {
        public virtual IReadOnlyList<IAiProvider> Snapshot => [];
        public virtual Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public virtual Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProviderDefinition>>([]);
        public virtual Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ProviderDefinition?>(null);
        public Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DefinitionOnlyManager(ProviderDefinition definition) : EmptyManager
    {
        public override Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>([definition]);
        public override Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<ProviderDefinition?>(string.Equals(id, definition.Id, StringComparison.OrdinalIgnoreCase) ? definition : null);
    }

    private sealed class Router : IAiRouter
    {
        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Task.FromResult(new RouterResult { Success = true, StatusCode = 200 });
        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Task.FromResult(new RouterResult { Success = true, StatusCode = 200 });
    }
}
