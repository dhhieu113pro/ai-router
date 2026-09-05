using System.Text.Json;
using AiRouter.Models;

namespace AiRouter.Tests;

public sealed class OpenAiModelContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Chat_request_deserializes_common_fields_and_preserves_unknown_properties()
    {
        const string json = """
        {
          "model":"coding",
          "messages":[{"role":"user","content":"hello"}],
          "stream":true,
          "temperature":0.2,
          "top_p":0.9,
          "max_tokens":123,
          "tools":[{"type":"function","function":{"name":"search"}}],
          "tool_choice":"auto",
          "response_format":{"type":"json_object"},
          "vendor_flag":"keep-me"
        }
        """;

        var request = JsonSerializer.Deserialize<ChatCompletionRequest>(json, Json)!;

        Assert.Equal("coding", request.Model);
        Assert.True(request.Stream);
        Assert.Equal("user", request.Messages.Single().Role);
        Assert.Equal(0.2, request.Temperature);
        Assert.Equal(123, request.MaxTokens);
        Assert.True(request.AdditionalProperties.ContainsKey("vendor_flag"));
        Assert.Equal("keep-me", request.AdditionalProperties["vendor_flag"].GetString());
    }

    [Fact]
    public void Chat_request_serializes_openai_snake_case_names()
    {
        var request = new ChatCompletionRequest
        {
            Model = "coding",
            Messages = [new ChatMessage { Role = "user", Content = JsonDocument.Parse("\"hi\"").RootElement.Clone() }],
            MaxTokens = 42,
            TopP = 0.8
        };

        var json = JsonSerializer.Serialize(request, Json);

        Assert.Contains("\"max_tokens\":42", json);
        Assert.Contains("\"top_p\":0.8", json);
        Assert.DoesNotContain("MaxTokens", json);
    }

    [Fact]
    public void Responses_request_preserves_input_instructions_tools_and_extensions()
    {
        const string json = """
        {
          "model":"balanced",
          "input":"hello",
          "instructions":"be concise",
          "stream":false,
          "tools":[{"type":"function","name":"search","parameters":{"type":"object"}}],
          "tool_choice":"auto",
          "max_output_tokens":321,
          "provider_extra":{"x":1}
        }
        """;

        var request = JsonSerializer.Deserialize<ResponsesRequest>(json, Json)!;

        Assert.Equal("balanced", request.Model);
        Assert.Equal("hello", request.Input.GetString());
        Assert.Equal("be concise", request.Instructions);
        Assert.Equal(321, request.MaxOutputTokens);
        Assert.True(request.AdditionalProperties.ContainsKey("provider_extra"));
    }

    [Fact]
    public void Model_info_uses_openai_model_shape()
    {
        var json = JsonSerializer.Serialize(new ModelInfo { Id = "coding" }, Json);
        Assert.Contains("\"id\":\"coding\"", json);
        Assert.Contains("\"object\":\"model\"", json);
    }
}
