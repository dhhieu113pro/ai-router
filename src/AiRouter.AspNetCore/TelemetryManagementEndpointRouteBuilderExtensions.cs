using System.Text.Json;
using AiRouter.AspNetCore;
using AiRouter.Configuration;
using AiRouter.Providers;
using AiRouter.Routing;
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

        endpoints.MapPost("/probe/cache", async (
            HttpContext context,
            IAiRouter router,
            IProviderManager providers,
            AiRouterOptions options) =>
        {
            if (!await BearerKeyAuthorizer.RequireAsync(context, bearerKey).ConfigureAwait(false)) return;

            CacheProbeRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<CacheProbeRequest>(Json, context.RequestAborted).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                request = null;
            }
            catch (BadHttpRequestException)
            {
                request = null;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Model) || request.Request.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "Model and a valid OpenAI-compatible request object are required.").ConfigureAwait(false);
                return;
            }

            if (request.Repeats < 1 || request.Repeats > options.CacheProbeMaxRepeats)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, $"Repeats must be between 1 and {options.CacheProbeMaxRepeats}.").ConfigureAwait(false);
                return;
            }

            var result = await CacheProbe.RunAsync(router, providers, request, context.RequestAborted).ConfigureAwait(false);
            await WriteJsonAsync(context, StatusCodes.Status200OK, result).ConfigureAwait(false);
        });

        return endpoints;
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string message) =>
        WriteJsonAsync(context, statusCode, new { error = new { message, type = "invalid_request_error" } });

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object value)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(value, Json), context.RequestAborted).ConfigureAwait(false);
    }
}
