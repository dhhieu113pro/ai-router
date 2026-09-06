using System.Text.Json;
using AiRouter.AspNetCore;
using AiRouter.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class AiRouterTelemetryManagementEndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAiRouterTelemetryManagementEndpoints(
        this IEndpointRouteBuilder endpoints,
        string? bearerKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/telemetry/summary", async (HttpContext context, IRouterTelemetry telemetry) =>
        {
            if (!await BearerKeyAuthorizer.RequireAsync(context, bearerKey).ConfigureAwait(false)) return;
            await WriteJsonAsync(context, StatusCodes.Status200OK, telemetry.Summary()).ConfigureAwait(false);
        });

        endpoints.MapGet("/telemetry/recent", async (HttpContext context, IRouterTelemetry telemetry) =>
        {
            if (!await BearerKeyAuthorizer.RequireAsync(context, bearerKey).ConfigureAwait(false)) return;
            await WriteJsonAsync(context, StatusCodes.Status200OK, telemetry.Recent()).ConfigureAwait(false);
        });

        return endpoints;
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object value)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(value, Json), context.RequestAborted).ConfigureAwait(false);
    }
}
