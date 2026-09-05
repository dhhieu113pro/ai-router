using System.Text;
using System.Text.Json;
using AiRouter.Models;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiResponsesCoverageEdgeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OpenAiResponsesTranslator _translator = new();

    [Theory]
    [InlineData("[{\"role\":1,\"content\":\"x\"}]")]
    [InlineData("[{\"role\":\"user\"}]")]
    [InlineData("[1]")]
    public void Invalid_structured_input_is_rejected(string input)
    {
        var request = Request(input);
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        var request = Request("null");
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Scalar_non_string_input_is_rejected()
    {
        var request = Request("123");
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Non_array_tools_are_rejected()
    {
        var request = Request("\"hi\"", "{\"type\":\"function\"}");
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("[{\"type\":\"web_search\",\"name\":\"search\"}]")]
    public void Unsupported_tool_shape_is_rejected(string tools)
    {
        var request = Request("\"hi\"", tools);
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Function_tool_without_name_is_rejected()
    {
        var request = Request("\"hi\"", "[{\"type\":\"function\"}]");
        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Function_tool_without_optional_description_or_parameters_is_supported()
    {
        var request = Request("\"hi\"", "[{\"type\":\"function\",\"name\":\"ping\"}]");

        var chat = _translator.ToChatRequest(request);
        var function = chat.Tools!.Value[0].GetProperty("function");

        Assert.Equal("ping", function.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, function.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, function.GetProperty("parameters").ValueKind);
    }

    [Fact]
    public void Response_without_id_model_choices_or_usage_uses_fallbacks()
    {
        using var chat = JsonDocument.Parse("{}");

        var response = _translator.ToResponsesResponse(chat.RootElement, "fallback-model");

        Assert.StartsWith("resp_", response.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.Equal("fallback-model", response.GetProperty("model").GetString());
        Assert.Equal(string.Empty, response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("usage").ValueKind);
    }

    [Fact]
    public void Response_skips_choices_without_message_or_content()
    {
        using var chat = JsonDocument.Parse("""
        {"choices":[{}, {"message":{}}, {"message":{"content":"found"}}]}
        """);

        var response = _translator.ToResponsesResponse(chat.RootElement, "model");

        Assert.Equal("found", response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Non_string_assistant_content_is_projected_as_raw_json()
    {
        using var chat = JsonDocument.Parse("""
        {"choices":[{"message":{"content":{"value":1}}}]}
        """);

        var response = _translator.ToResponsesResponse(chat.RootElement, "model");

        Assert.Equal("{\"value\":1}", response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Usage_with_missing_or_non_numeric_fields_returns_null_fields()
    {
        using var chat = JsonDocument.Parse("""
        {"choices":[],"usage":{"prompt_tokens":"bad","total_tokens":4}}
        """);

        var response = _translator.ToResponsesResponse(chat.RootElement, "model");
        var usage = response.GetProperty("usage");

        Assert.Equal(JsonValueKind.Null, usage.GetProperty("input_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("output_tokens").ValueKind);
        Assert.Equal(4, usage.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public async Task Sse_ignores_non_data_empty_and_chunks_without_text_delta()
    {
        const string input = "event: ignored\n\ndata:\n\ndata: {}\n\ndata: {\"choices\":[{}]}\n\ndata: {\"choices\":[{\"delta\":{}}]}\n\ndata: [DONE]\n\n";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var translated = _translator.ToResponsesStream(source);
        using var reader = new StreamReader(translated);

        var output = await reader.ReadToEndAsync();

        Assert.DoesNotContain("response.output_text.delta", output, StringComparison.Ordinal);
        Assert.Contains("response.completed", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sse_end_of_stream_without_done_completes_cleanly()
    {
        const string input = "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n\n";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var translated = _translator.ToResponsesStream(source);
        using var reader = new StreamReader(translated);

        var output = await reader.ReadToEndAsync();

        Assert.Contains("\"delta\":\"x\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("response.completed", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_sse_json_faults_translated_stream()
    {
        const string input = "data: not-json\n\n";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var translated = _translator.ToResponsesStream(source);
        using var reader = new StreamReader(translated);

        await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Sse_caller_cancellation_faults_translated_stream()
    {
        const string input = "data: {\"choices\":[]}\n\n";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var translated = _translator.ToResponsesStream(source, cts.Token);
        using var reader = new StreamReader(translated);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadToEndAsync());
    }

    private static ResponsesRequest Request(string inputJson, string? toolsJson = null)
    {
        var tools = toolsJson is null ? string.Empty : $",\"tools\":{toolsJson}";
        return JsonSerializer.Deserialize<ResponsesRequest>(
            $"{{\"model\":\"m\",\"input\":{inputJson}{tools}}}", Json)!;
    }
}
