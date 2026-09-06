using System.Text.Json;
using AiRouter.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace AiRouter.AspNetCore.Tests;

public sealed class AffinityRoutingApiTests
{
    [Fact]
    public void Header_takes_precedence_over_user_and_prefix()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-AiRouter-Session"] = "session-123";
        var body = JsonDocument.Parse("{\"user\":\"user-456\",\"messages\":[{\"role\":\"system\",\"content\":\"stable\"}]}").RootElement.Clone();

        var resolved = AffinityKeyResolver.Resolve(context, "route", body);

        Assert.Equal("header", resolved.AffinitySource);
        Assert.NotEqual("session-123", resolved.AffinityKey);
        Assert.Equal(64, resolved.AffinityKey!.Length);
    }

    [Fact]
    public void User_takes_precedence_over_prefix()
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse("{\"user\":\"user-456\",\"messages\":[{\"role\":\"system\",\"content\":\"stable\"}]}").RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("user", resolved.AffinitySource);
    }

    [Fact]
    public void Stable_prefix_is_used_when_explicit_identity_is_missing()
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse("{\"instructions\":\"be concise\",\"input\":[{\"role\":\"developer\",\"content\":\"stable tool contract\"},{\"role\":\"user\",\"content\":\"changes every turn\"}]}").RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("prefix", resolved.AffinitySource);
        Assert.Equal(64, resolved.AffinityKey!.Length);
    }

    [Fact]
    public void Route_source_is_used_when_no_stable_identity_exists()
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse("{\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}").RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("route", resolved.AffinitySource);
        Assert.Null(resolved.AffinityKey);
    }
}
