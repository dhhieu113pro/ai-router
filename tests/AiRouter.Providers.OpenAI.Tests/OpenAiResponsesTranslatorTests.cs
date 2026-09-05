using System.Text;
using System.Text.Json;
using AiRouter.Models;
using AiRouter.Providers.OpenAI;

namespace AiRouter.Providers.OpenAI.Tests;

public sealed class OpenAiResponsesTranslatorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OpenAiResponsesTranslator _translator = new();

    [Fact]
    public void String_input_and_instructions_become_chat_messages()
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>("""
        {"model":"coding","input":"hello","instructions":"be concise","max_output_tokens":50,"temperature":0.2}
        """, Json)!;

        var chat = _translator.ToChatRequest(request);

        Assert.Equal("coding", chat.Model);
        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal("system", chat.Messages[0].Role);
        Assert.Equal("be concise", chat.Messages[0].Content.GetString());
        Assert.Equal("user", chat.Messages[1].Role);
        Assert.Equal("hello", chat.Messages[1].Content.GetString());
        Assert.Equal(50, chat.MaxTokens);
    }

    [Fact]
    public void Structured_input_preserves_roles_and_content()
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>("""
        {"model":"coding","input":[{"role":"user","content":"hello"},{"role":"assistant","content":"hi"}]}
        """, Json)!;

        var chat = _translator.ToChatRequest(request);

        Assert.Equal(["user", "assistant"], chat.Messages.Select(message => message.Role));
        Assert.Equal("hello", chat.Messages[0].Content.GetString());
    }

    [Fact]
    public void Function_tools_are_converted_to_chat_function_shape()
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>("""
        {"model":"coding","input":"hi","tools":[{"type":"function","name":"search","description":"Search","parameters":{"type":"object"}}],"tool_choice":"auto"}
        """, Json)!;

        var chat = _translator.ToChatRequest(request);
        var tool = chat.Tools!.Value.EnumerateArray().Single();

        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("search", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("auto", chat.ToolChoice!.Value.GetString());
    }

    [Fact]
    public void Unsupported_responses_feature_is_rejected_explicitly()
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>("""
        {"model":"coding","input":"hi","background":true}
        """, Json)!;

        Assert.Throws<ResponsesTranslationException>(() => _translator.ToChatRequest(request));
    }

    [Fact]
    public void Chat_response_is_projected_to_responses_output()
    {
        using var chat = JsonDocument.Parse("""
        {"id":"chat-1","model":"actual","choices":[{"message":{"role":"assistant","content":"hello"}}],"usage":{"prompt_tokens":2,"completion_tokens":1,"total_tokens":3}}
        """);

        var response = _translator.ToResponsesResponse(chat.RootElement, "actual");

        Assert.Equal("response", response.GetProperty("object").GetString());
        Assert.Equal("actual", response.GetProperty("model").GetString());
        Assert.Equal("hello", response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(3, response.GetProperty("usage").GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public async Task Chat_sse_is_translated_to_responses_delta_and_completed_events()
    {
        const string input = "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var translated = _translator.ToResponsesStream(source);
        using var reader = new StreamReader(translated);

        var output = await reader.ReadToEndAsync();

        Assert.Contains("response.output_text.delta", output);
        Assert.Contains("\"delta\":\"Hi\"", output);
        Assert.Contains("response.completed", output);
    }
}
