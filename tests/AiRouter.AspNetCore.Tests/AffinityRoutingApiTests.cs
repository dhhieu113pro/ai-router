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
        Assert.Equal("user", AffinityKeyResolver.Resolve(context, "route", body).AffinitySource);
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
    public void Stable_prefix_supports_array_content_and_ignores_non_text_parts()
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse("""
        {"messages":[{"role":"system","content":[" first ",{"text":"second"},{"image_url":"ignored"},42]},{"role":"developer","content":[{"text":" third "}]},{"role":"user","content":"stop"}]}
        """).RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("prefix", resolved.AffinitySource);
        Assert.Equal(64, resolved.AffinityKey!.Length);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"instructions\":42}")]
    [InlineData("{\"instructions\":[]}")]
    [InlineData("{\"messages\":{}}")]
    [InlineData("{\"input\":{}}")]
    [InlineData("{\"messages\":[{\"role\":42,\"content\":\"x\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"system\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"system\",\"content\":42}]}")]
    [InlineData("{\"messages\":[{\"role\":\"system\",\"content\":[]}]}")]
    public void Invalid_or_non_text_prefix_shapes_fall_back_to_route(string json)
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse(json).RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("route", resolved.AffinitySource);
        Assert.Null(resolved.AffinityKey);
    }

    [Fact]
    public void Invalid_leading_message_shape_falls_back_to_route_identity()
    {
        var context = new DefaultHttpContext();
        var body = JsonDocument.Parse("{\"messages\":[42,{\"content\":\"missing role\"}]}").RootElement.Clone();
        var resolved = AffinityKeyResolver.Resolve(context, "route", body);
        Assert.Equal("route", resolved.AffinitySource);
        Assert.Null(resolved.AffinityKey);
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
