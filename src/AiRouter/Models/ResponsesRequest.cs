using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRouter.Models;

public sealed class ResponsesRequest
{
    public string Model { get; set; } = "";
    public JsonElement Input { get; set; }
    public string? Instructions { get; set; }
    public bool Stream { get; set; }
    public JsonElement? Tools { get; set; }
    public JsonElement? ToolChoice { get; set; }
    public int? MaxOutputTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.Ordinal);
}
