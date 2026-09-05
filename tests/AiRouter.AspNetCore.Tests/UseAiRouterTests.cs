using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class UseAiRouterTests
{
    [Fact]
    public async Task UseAiRouter_maps_default_v1_routes_with_only_AddAiRouter_registration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();

        await using var app = builder.Build();
        app.UseAiRouter();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("/api")]
    [InlineData("api/")]
    [InlineData("/api/")]
    public async Task UseAiRouter_normalizes_custom_prefix(string prefix)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();

        await using var app = builder.Build();
        app.UseAiRouter(prefix);
        await app.StartAsync();

        var client = app.GetTestClient();
        var prefixedResponse = await client.GetAsync("/api/v1/models");
        var defaultResponse = await client.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, prefixedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultResponse.StatusCode);
    }

    [Fact]
    public async Task UseAiRouter_keeps_bearer_key_support()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAiRouter();

        await using var app = builder.Build();
        app.UseAiRouter(bearerKey: "secret");
        await app.StartAsync();

        var client = app.GetTestClient();
        var unauthorized = await client.GetAsync("/v1/models");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Authorization = new("Bearer", "secret");
        var authorized = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }
}
