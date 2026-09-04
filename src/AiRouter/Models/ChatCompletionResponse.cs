using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRouter.Models;

public sealed class ChatCompletionResponse
{
    public string? Id { get; set; }
    public string? Object { get; set; }
    public long? Created { get; set; }
    public string? Model { get; set; }
    public JsonElement? Choices { get; set; }
    public JsonElement? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}
