using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class AiRouterEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAiRouterOpenAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/v1/chat/completions", OpenAiEndpointHandlers.ChatAsync);
        endpoints.MapPost("/v1/responses", OpenAiEndpointHandlers.ResponsesAsync);
        endpoints.MapGet("/v1/models", OpenAiEndpointHandlers.ModelsAsync);

        return endpoints;
    }
}

internal static class OpenAiEndpointHandlers
{
    public static Task ChatAsync(HttpContext context, IAiRouter router) =>
        RouteAsync(context, router.ChatAsync);

    public static Task ResponsesAsync(HttpContext context, IAiRouter router) =>
        RouteAsync(context, router.ResponsesAsync);

    public static async Task ModelsAsync(
        HttpContext context,
        IProviderManager providerManager,
        IRouteStore routeStore)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var routes = await routeStore.ListAsync(context.RequestAborted).ConfigureAwait(false);
        foreach (var route in routes.Where(static route => route.Enabled))
            ids.Add(route.Id);

        var providers = await providerManager.ListAsync(context.RequestAborted).ConfigureAwait(false);
        var anyDirectModel = false;
        foreach (var provider in providers.Where(static provider => provider.Enabled))
        {
            IReadOnlyList<string> models = provider.Models ?? [];
            if (models.Count == 0 && provider.DiscoverModels)
            {
                try
                {
                    models = await providerManager.ListModelsAsync(provider.Id, context.RequestAborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    models = [];
                }
            }

            foreach (var model in models.Where(static model => !string.IsNullOrWhiteSpace(model)))
            {
                ids.Add($"{provider.Id}/{model}");
                anyDirectModel = true;
            }
        }

        if (anyDirectModel)
            ids.Add("all");

        var data = ids
            .Order(StringComparer.Ordinal)
            .Select(static id => new
            {
                id,
                @object = "model",
                created = 0,
                owned_by = "ai-router"
            })
            .ToArray();

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { @object = "list", data }),
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task RouteAsync(
        HttpContext context,
        Func<string, JsonElement, bool, CancellationToken, Task<RouterResult>> send)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "Request body must be valid JSON.", "invalid_request_error")
                .ConfigureAwait(false);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("model", out var modelElement) ||
                modelElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(modelElement.GetString()))
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "Model is required.", "invalid_request_error")
                    .ConfigureAwait(false);
                return;
            }

            var model = modelElement.GetString()!;
            var stream = root.TryGetProperty("stream", out var streamElement) &&
                         streamElement.ValueKind == JsonValueKind.True;

            var result = await send(model, root.Clone(), stream, context.RequestAborted).ConfigureAwait(false);
            await WriteRouterResultAsync(context, result).ConfigureAwait(false);
        }
    }

    private static async Task WriteRouterResultAsync(HttpContext context, RouterResult result)
    {
        if (!result.Success)
        {
            var type = result.FailureKind switch
            {
                ProviderFailureKind.InvalidRequest => "invalid_request_error",
                ProviderFailureKind.RateLimited => "rate_limit_error",
                _ => "server_error"
            };

            await WriteErrorAsync(
                context,
                result.StatusCode > 0 ? result.StatusCode : StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "AI routing request failed.",
                type).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = result.StatusCode > 0 ? result.StatusCode : StatusCodes.Status200OK;
        if (!string.IsNullOrWhiteSpace(result.ProviderId))
            context.Response.Headers["X-AiRouter-Provider"] = result.ProviderId;
        if (!string.IsNullOrWhiteSpace(result.Model))
            context.Response.Headers["X-AiRouter-Model"] = result.Model;

        if (result.Stream is not null)
        {
            context.Response.ContentType = result.ContentType ?? "text/event-stream";
            await using var stream = result.Stream;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = result.ContentType ?? "application/json";
        if (result.Body is JsonElement body)
            await context.Response.WriteAsync(body.GetRawText(), context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message, string type)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            error = new
            {
                message,
                type,
                param = (string?)null,
                code = (string?)null
            }
        });
        await context.Response.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
    }
}
