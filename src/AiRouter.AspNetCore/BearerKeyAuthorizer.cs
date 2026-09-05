using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace AiRouter.AspNetCore;

internal static class BearerKeyAuthorizer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsAuthorized(HttpContext context, string? expectedKey)
    {
        if (string.IsNullOrEmpty(expectedKey))
            return true;

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var supplied = header[prefix.Length..].Trim();
        if (supplied.Length == 0)
            return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    public static async Task<bool> RequireAsync(HttpContext context, string? expectedKey)
    {
        if (IsAuthorized(context, expectedKey))
            return true;

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                error = new
                {
                    message = "Invalid or missing API key.",
                    type = "invalid_request_error",
                    param = (string?)null,
                    code = "invalid_api_key"
                }
            }, Json),
            context.RequestAborted).ConfigureAwait(false);
        return false;
    }
}
