using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRouter.Models;

public sealed class ResponsesResponse
{
    public string? Id { get; set; }
    public string Object { get; set; } = "response";
    public string? Model { get; set; }
    public JsonElement? Output { get; set; }
    public JsonElement? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}
