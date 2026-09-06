using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class CacheProbeApiTests
{
    [Fact]
    public async Task Probe_defaults_to_three_repeats_and_reports_cache_ratio()
    {
        var router = new FakeRouter(
            Result("p1", 0),
            Result("p1", 80),
            Result("p1", 90));
        await using var app = await StartAsync(router);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");

        var response = await client.PostAsJsonAsync("/probe/cache", new
        {
            model = "coding",
            request = new { messages = new[] { new { role = "user", content = "ping" } } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("repeats").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("attempts").GetArrayLength());
        Assert.False(json.RootElement.GetProperty("targetChanged").GetBoolean());
        Assert.True(json.RootElement.GetProperty("cacheRatio").GetDecimal() > 0m);
    }

    [Fact]
    public async Task Probe_rejects_repeat_count_above_configured_max()
    {
        await using var app = await StartAsync(new FakeRouter(Result("p1", 0)));
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");

        var response = await client.PostAsJsonAsync("/probe/cache", new
        {
            model = "coding",
            repeats = 6,
            request = new { messages = Array.Empty<object>() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Probe_reports_target_instability_and_recommends_sticky_pinning()
    {
        var router = new FakeRouter(Result("p1", 0), Result("p2", 0));
        await using var app = await StartAsync(router);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");

        var response = await client.PostAsJsonAsync("/probe/cache", new
        {
            model = "coding",
            repeats = 2,
            request = new { messages = Array.Empty<object>() }
        });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("targetChanged").GetBoolean());
        Assert.Contains("target_changed", json.RootElement.GetProperty("diagnostics").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("Sticky", json.RootElement.GetProperty("recommendation").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Probe_requires_admin_authorization()
    {
        await using var app = await StartAsync(new FakeRouter(Result("p1", 0)));
        var response = await app.GetTestClient().PostAsJsonAsync("/probe/cache", new
        {
            model = "coding",
            request = new { messages = Array.Empty<object>() }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RouterResult Result(string provider, int cached) => new()
    {
        Success = true,
        StatusCode = 200,
        ProviderId = provider,
        Model = "model",
        Body = JsonSerializer.SerializeToElement(new
        {
            usage = new
            {
                prompt_tokens = 100,
                completion_tokens = 10,
                total_tokens = 110,
                prompt_tokens_details = new { cached_tokens = cached }
            }
        }),
        AffinityClassification = cached == 0 ? "miss" : "hit",
        AffinitySource = "probe",
        AttemptCount = 1
    };

    private static async Task<WebApplication> StartAsync(IAiRouter router)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();
        builder.Services.AddSingleton(router);

        var app = builder.Build();
        app.MapAiRouterTelemetryManagementEndpoints("admin");
        await app.StartAsync();
        return app;
    }

    private sealed class FakeRouter(params RouterResult[] results) : IAiRouter
    {
        private int _index;
        private Task<RouterResult> Next()
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(results[Math.Min(index, results.Length - 1)]);
        }

        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Next();
        public Task<RouterResult> ChatAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) => Next();
        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) => Next();
        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, RouterRequestContext? requestContext, bool stream = false, CancellationToken ct = default) => Next();
    }
}
