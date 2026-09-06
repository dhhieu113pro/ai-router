using System.Text.Json;

namespace AiRouter.Providers;

public static class ProviderUsageParser
{
    public static ProviderUsage? ParseOpenAiCompatible(JsonElement? body)
    {
        if (body is not JsonElement root || root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        int? input = Int(usage, "prompt_tokens") ?? Int(usage, "input_tokens");
        int? output = Int(usage, "completion_tokens") ?? Int(usage, "output_tokens");
        int? total = Int(usage, "total_tokens") ?? (input is not null && output is not null ? input + output : null);
        int? cached = null;
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) && promptDetails.ValueKind == JsonValueKind.Object)
            cached = Int(promptDetails, "cached_tokens");
        if (cached is null && usage.TryGetProperty("input_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object)
            cached = Int(inputDetails, "cached_tokens");

        var cacheWrite = Int(usage, "cache_creation_input_tokens") ?? Int(usage, "cache_write_tokens");
        var cost = Decimal(usage, "cost") ?? Decimal(root, "cost");
        return new ProviderUsage(input, output, total, cached, cacheWrite, cost);
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;

    private static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed) ? parsed : null;
}
