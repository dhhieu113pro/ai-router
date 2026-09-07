using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRouter.Routing;
using Microsoft.AspNetCore.Http;

namespace AiRouter.AspNetCore;

public static class AffinityKeyResolver
{
    public static RouterRequestContext Resolve(HttpContext context, string routeId, JsonElement body)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = context.Request.Headers["X-AiRouter-Session"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
            return new RouterRequestContext(Hash(header.Trim()), "header", context.TraceIdentifier);

        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.String)
        {
            var value = user.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return new RouterRequestContext(Hash(value.Trim()), "user", context.TraceIdentifier);
        }

        var prefix = StablePrefix(body);
        if (!string.IsNullOrWhiteSpace(prefix))
            return new RouterRequestContext(Hash(prefix), "prefix", context.TraceIdentifier);

        return new RouterRequestContext(null, "route", context.TraceIdentifier);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? StablePrefix(JsonElement body)
    {
        var parts = new List<string>();
        if (body.ValueKind != JsonValueKind.Object) return null;

        if (body.TryGetProperty("instructions", out var instructions))
        {
            var text = TextOf(instructions);
            if (!string.IsNullOrWhiteSpace(text)) parts.Add("instructions:" + Normalize(text));
        }

        if (body.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            AddLeadingStableMessages(messages, parts);

        if (body.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array)
            AddLeadingStableMessages(input, parts);

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static void AddLeadingStableMessages(JsonElement items, List<string> parts)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("role", out var roleElement) || roleElement.ValueKind != JsonValueKind.String)
                return;
            var role = roleElement.GetString();
            if (role is not ("system" or "developer")) break;
            if (!item.TryGetProperty("content", out var content)) continue;
            var text = TextOf(content);
            if (!string.IsNullOrWhiteSpace(text)) parts.Add(role + ":" + Normalize(text));
        }
    }

    private static string? TextOf(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Array) return null;
        var parts = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) parts.Add(item.GetString()!);
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                parts.Add(text.GetString()!);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
