using System.Net;
using System.Net.Http.Headers;
using AiRouter.Providers;
using AiRouter.Routing;
using AiRouter.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class TelemetryManagementApiTests
{
    [Fact]
    public async Task Telemetry_endpoints_require_admin_key_and_return_safe_shape()
    {
        await using var app = await StartAsync("admin-secret");
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/telemetry/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/telemetry/recent")).StatusCode);

        var telemetry = app.Services.GetRequiredService<IRouterTelemetry>();
        telemetry.Record(new RouterTelemetryRecord(
            DateTimeOffset.UtcNow,
            "coding",
            "p1",
            "model",
            RoutingStrategy.Sticky,
            false,
            true,
            false,
            "hit",
            1,
            TimeSpan.FromMilliseconds(12),
            new ProviderUsage(100, 10, 110, 80, null, null),
            0.01m,
            "estimated",
            true,
            200,
            ProviderFailureKind.None));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-secret");

        var summary = await client.GetAsync("/telemetry/summary");
        var summaryText = await summary.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Contains("cacheRatio", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cacheCoverage", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("totalCost", summaryText, StringComparison.OrdinalIgnoreCase);

        var recent = await client.GetAsync("/telemetry/recent");
        var recentText = await recent.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        Assert.DoesNotContain("prompt", recentText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", recentText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("affinityKey", recentText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WebApplication> StartAsync(string adminKey)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();

        var app = builder.Build();
        app.MapAiRouterTelemetryManagementEndpoints(adminKey);
        await app.StartAsync();
        return app;
    }
}
