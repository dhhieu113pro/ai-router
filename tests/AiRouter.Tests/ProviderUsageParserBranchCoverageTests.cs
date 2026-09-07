using System.Text.Json;
using AiRouter.Providers;

namespace AiRouter.Tests;

public sealed class ProviderUsageParserBranchCoverageTests
{
    [Fact]
    public void Parse_returns_null_for_missing_or_invalid_usage_shapes()
    {
        Assert.Null(ProviderUsageParser.ParseOpenAiCompatible(null));
        Assert.Null(Parse("[]"));
        Assert.Null(Parse("{}"));
        Assert.Null(Parse("{\"usage\":1}"));
    }

    [Fact]
    public void Parse_empty_usage_object_has_all_unknown_values()
    {
        var usage = Parse("{\"usage\":{}}")!;
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.TotalTokens);
        Assert.Null(usage.CachedInputTokens);
        Assert.Null(usage.CacheWriteTokens);
        Assert.Null(usage.ReportedCost);
    }

    [Fact]
    public void Parse_supports_responses_token_names_and_computes_total()
    {
        var usage = Parse("""
        {"usage":{"input_tokens":7,"output_tokens":3,"input_tokens_details":{"cached_tokens":2},"cache_write_tokens":4}}
        """)!;
        Assert.Equal(7, usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Equal(10, usage.TotalTokens);
        Assert.Equal(2, usage.CachedInputTokens);
        Assert.Equal(4, usage.CacheWriteTokens);
    }

    [Fact]
    public void Parse_prefers_prompt_details_and_usage_level_cost()
    {
        var usage = Parse("""
        {"cost":9.9,"usage":{"prompt_tokens":8,"completion_tokens":2,"total_tokens":11,"prompt_tokens_details":{"cached_tokens":5},"input_tokens_details":{"cached_tokens":1},"cache_creation_input_tokens":6,"cost":0.25}}
        """)!;
        Assert.Equal(8, usage.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
        Assert.Equal(11, usage.TotalTokens);
        Assert.Equal(5, usage.CachedInputTokens);
        Assert.Equal(6, usage.CacheWriteTokens);
        Assert.Equal(0.25m, usage.ReportedCost);
    }

    [Fact]
    public void Parse_falls_back_to_root_cost_when_usage_cost_is_invalid()
    {
        var usage = Parse("""
        {"cost":0.5,"usage":{"prompt_tokens":1,"completion_tokens":2,"cost":"unknown","prompt_tokens_details":null,"input_tokens_details":[]}}
        """)!;
        Assert.Null(usage.CachedInputTokens);
        Assert.Equal(0.5m, usage.ReportedCost);
    }

    [Fact]
    public void Parse_leaves_total_null_when_output_is_unknown()
    {
        var usage = Parse("{\"usage\":{\"input_tokens\":7}}")!;
        Assert.Equal(7, usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.TotalTokens);
    }

    [Fact]
    public void Parse_leaves_total_null_when_one_side_is_unknown_and_rejects_non_integer_tokens()
    {
        var usage = Parse("""
        {"usage":{"prompt_tokens":"bad","input_tokens":2147483648,"completion_tokens":2.5,"output_tokens":3}}
        """)!;
        Assert.Null(usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Null(usage.TotalTokens);
    }

    [Fact]
    public void Parse_handles_numeric_fields_that_are_absent_or_invalid()
    {
        var usage = Parse("""
        {"usage":{"prompt_tokens_details":{"cached_tokens":"bad"},"input_tokens_details":{"cached_tokens":2.5},"cache_creation_input_tokens":"bad","cache_write_tokens":2.5,"cost":1e1000,"total_tokens":"bad"}}
        """)!;
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.TotalTokens);
        Assert.Null(usage.CachedInputTokens);
        Assert.Null(usage.CacheWriteTokens);
        Assert.Null(usage.ReportedCost);
    }

    private static ProviderUsage? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ProviderUsageParser.ParseOpenAiCompatible(document.RootElement);
    }
}
