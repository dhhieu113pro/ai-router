using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRouter.Models;

public sealed class ChatCompletionRequest
{
    public string Model { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = [];
    public bool Stream { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public JsonElement? Stop { get; set; }
    public JsonElement? Tools { get; set; }
    public JsonElement? ToolChoice { get; set; }
    public JsonElement? ResponseFormat { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ChatMessage
{
    public string Role { get; set; } = "";
    public JsonElement Content { get; set; }
    public string? Name { get; set; }
    public JsonElement? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}
