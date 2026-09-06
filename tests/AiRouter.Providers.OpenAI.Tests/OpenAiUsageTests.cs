using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiUsageTests
{
    [Fact]
    public void Parses_chat_usage_and_cached_tokens()
    {
        var body = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":1000,\"completion_tokens\":100,\"total_tokens\":1100,\"prompt_tokens_details\":{\"cached_tokens\":800}}}").RootElement.Clone();
        var usage = ProviderUsageParser.ParseOpenAiCompatible(body)!;
        Assert.Equal(1000, usage.InputTokens);
        Assert.Equal(100, usage.OutputTokens);
        Assert.Equal(1100, usage.TotalTokens);
        Assert.Equal(800, usage.CachedInputTokens);
    }

    [Fact]
    public void Parses_responses_usage_and_reported_cost()
    {
        var body = JsonDocument.Parse("{\"usage\":{\"input_tokens\":200,\"output_tokens\":50,\"input_tokens_details\":{\"cached_tokens\":120},\"cost\":0.0123}}").RootElement.Clone();
        var usage = ProviderUsageParser.ParseOpenAiCompatible(body)!;
        Assert.Equal(200, usage.InputTokens);
        Assert.Equal(50, usage.OutputTokens);
        Assert.Equal(250, usage.TotalTokens);
        Assert.Equal(120, usage.CachedInputTokens);
        Assert.Equal(0.0123m, usage.ReportedCost);
    }

    [Fact]
    public void Missing_usage_returns_null()
    {
        var body = JsonDocument.Parse("{\"id\":\"x\"}").RootElement.Clone();
        Assert.Null(ProviderUsageParser.ParseOpenAiCompatible(body));
    }

    [Fact]
    public void Provider_response_exposes_normalized_usage_without_mutating_body()
    {
        var body = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2}}").RootElement.Clone();
        var response = new ProviderResponse { Success = true, StatusCode = 200, Body = body };
        Assert.Equal(10, response.Usage!.InputTokens);
        Assert.Equal(12, response.Usage.TotalTokens);
        Assert.Equal(body.GetRawText(), response.Body!.Value.GetRawText());
    }
}
