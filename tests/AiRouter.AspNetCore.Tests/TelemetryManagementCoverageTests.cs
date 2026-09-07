using System.Net;
using System.Net.Http.Headers;
using AiRouter.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class TelemetryManagementCoverageTests
{
    [Fact]
    public async Task Summary_and_recent_return_authorized_payloads()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");

        var summary = await client.GetAsync("/telemetry/summary");
        var recent = await client.GetAsync("/telemetry/recent");

        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        Assert.Equal("application/json", summary.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", recent.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Summary_and_recent_require_authorization()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/telemetry/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/telemetry/recent")).StatusCode);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();
        builder.Services.AddAiRouterAspNetCore();
        builder.Services.AddSingleton<IRouterTelemetry>(new InMemoryRouterTelemetry());

        var app = builder.Build();
        app.MapAiRouterTelemetryManagementEndpoints("admin");
        await app.StartAsync();
        return app;
    }
}
