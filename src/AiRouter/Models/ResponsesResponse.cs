using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRouter.Models;

public sealed class ResponsesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; } = "response";

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("output")]
    public JsonElement? Output { get; set; }

    [JsonPropertyName("usage")]
    public JsonElement? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}
