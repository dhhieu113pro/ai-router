using System.Text.Json;
using AiRouter.AspNetCore;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class AiRouterManagementEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAiRouterManagementEndpoints(
        this IEndpointRouteBuilder endpoints,
        string? bearerKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/providers", (HttpContext context, IProviderManager manager) =>
            ManagementHandlers.ListProvidersAsync(context, manager, bearerKey));
        endpoints.MapPost("/providers", (HttpContext context, IProviderManager manager) =>
            ManagementHandlers.AddProviderAsync(context, manager, bearerKey));
        endpoints.MapGet("/providers/{id}", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.GetProviderAsync(context, id, manager, bearerKey));
        endpoints.MapPut("/providers/{id}", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.UpdateProviderAsync(context, id, manager, bearerKey));
        endpoints.MapDelete("/providers/{id}", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.DeleteProviderAsync(context, id, manager, bearerKey));
        endpoints.MapPost("/providers/{id}/enable", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.SetProviderEnabledAsync(context, id, true, manager, bearerKey));
        endpoints.MapPost("/providers/{id}/disable", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.SetProviderEnabledAsync(context, id, false, manager, bearerKey));
        endpoints.MapPost("/providers/{id}/test", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.TestProviderAsync(context, id, manager, bearerKey));
        endpoints.MapGet("/providers/{id}/models", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.ProviderModelsAsync(context, id, manager, bearerKey));
        endpoints.MapGet("/providers/{id}/health", (HttpContext context, string id, IProviderManager manager) =>
            ManagementHandlers.ProviderHealthAsync(context, id, manager, bearerKey));

        endpoints.MapGet("/routes", (HttpContext context, IRouteStore store) =>
            ManagementHandlers.ListRoutesAsync(context, store, bearerKey));
        endpoints.MapPost("/routes", (HttpContext context, IRouteStore store) =>
            ManagementHandlers.AddRouteAsync(context, store, bearerKey));
        endpoints.MapGet("/routes/{id}", (HttpContext context, string id, IRouteStore store) =>
            ManagementHandlers.GetRouteAsync(context, id, store, bearerKey));
        endpoints.MapPut("/routes/{id}", (HttpContext context, string id, IRouteStore store) =>
            ManagementHandlers.UpdateRouteAsync(context, id, store, bearerKey));
        endpoints.MapDelete("/routes/{id}", (HttpContext context, string id, IRouteStore store) =>
            ManagementHandlers.DeleteRouteAsync(context, id, store, bearerKey));

        return endpoints;
    }
}

internal static class ManagementHandlers
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task ListProvidersAsync(HttpContext context, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var providers = await manager.ListAsync(context.RequestAborted).ConfigureAwait(false);
        await JsonAsync(context, StatusCodes.Status200OK, providers.Select(static provider => provider.Redacted()).ToArray()).ConfigureAwait(false);
    }

    public static async Task AddProviderAsync(HttpContext context, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var provider = await ReadAsync<ProviderDefinition>(context).ConfigureAwait(false);
        if (provider is null) return;
        try
        {
            var created = await manager.AddAsync(provider, context.RequestAborted).ConfigureAwait(false);
            await JsonAsync(context, StatusCodes.Status201Created, created.Redacted()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message).ConfigureAwait(false);
        }
    }

    public static async Task GetProviderAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var provider = await manager.GetAsync(id, context.RequestAborted).ConfigureAwait(false);
        if (provider is null) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); return; }
        await JsonAsync(context, StatusCodes.Status200OK, provider.Redacted()).ConfigureAwait(false);
    }

    public static async Task UpdateProviderAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var provider = await ReadAsync<ProviderDefinition>(context).ConfigureAwait(false);
        if (provider is null) return;
        if (!string.Equals(id, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, "Provider id in the request body must match the route id.").ConfigureAwait(false);
            return;
        }
        try
        {
            var updated = await manager.UpdateAsync(id, provider, context.RequestAborted).ConfigureAwait(false);
            await JsonAsync(context, StatusCodes.Status200OK, updated.Redacted()).ConfigureAwait(false);
        }
        catch (KeyNotFoundException) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message).ConfigureAwait(false);
        }
    }

    public static async Task DeleteProviderAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        await manager.DeleteAsync(id, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public static async Task SetProviderEnabledAsync(HttpContext context, string id, bool enabled, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        try
        {
            var provider = await manager.SetEnabledAsync(id, enabled, context.RequestAborted).ConfigureAwait(false);
            await JsonAsync(context, StatusCodes.Status200OK, provider.Redacted()).ConfigureAwait(false);
        }
        catch (KeyNotFoundException) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); }
    }

    public static async Task TestProviderAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        try { await JsonAsync(context, StatusCodes.Status200OK, await manager.TestAsync(id, context.RequestAborted).ConfigureAwait(false)).ConfigureAwait(false); }
        catch (KeyNotFoundException) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); }
    }

    public static async Task ProviderModelsAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        try { await JsonAsync(context, StatusCodes.Status200OK, await manager.ListModelsAsync(id, context.RequestAborted).ConfigureAwait(false)).ConfigureAwait(false); }
        catch (KeyNotFoundException) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); }
    }

    public static async Task ProviderHealthAsync(HttpContext context, string id, IProviderManager manager, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var definition = await manager.GetAsync(id, context.RequestAborted).ConfigureAwait(false);
        if (definition is null) { await NotFoundAsync(context, "Provider", id).ConfigureAwait(false); return; }
        var provider = manager.Snapshot.FirstOrDefault(p => string.Equals(p.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            await JsonAsync(context, StatusCodes.Status200OK, new { status = definition.Enabled ? "Unknown" : "Disabled", consecutiveFailures = 0 }).ConfigureAwait(false);
            return;
        }
        var health = provider.Health;
        await JsonAsync(context, StatusCodes.Status200OK, new
        {
            status = health.Status.ToString(),
            health.ConsecutiveFailures,
            health.CooldownUntil,
            health.LastRequestAt,
            health.LastSuccessAt,
            health.LastFailureAt,
            health.LastError,
            health.LastLatency
        }).ConfigureAwait(false);
    }

    public static async Task ListRoutesAsync(HttpContext context, IRouteStore store, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        await JsonAsync(context, StatusCodes.Status200OK, await store.ListAsync(context.RequestAborted).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public static async Task AddRouteAsync(HttpContext context, IRouteStore store, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var route = await ReadAsync<RouteDefinition>(context).ConfigureAwait(false);
        if (route is null) return;
        await store.UpsertAsync(route, context.RequestAborted).ConfigureAwait(false);
        await JsonAsync(context, StatusCodes.Status201Created, route).ConfigureAwait(false);
    }

    public static async Task GetRouteAsync(HttpContext context, string id, IRouteStore store, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var route = await store.GetAsync(id, context.RequestAborted).ConfigureAwait(false);
        if (route is null) { await NotFoundAsync(context, "Route", id).ConfigureAwait(false); return; }
        await JsonAsync(context, StatusCodes.Status200OK, route).ConfigureAwait(false);
    }

    public static async Task UpdateRouteAsync(HttpContext context, string id, IRouteStore store, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        var route = await ReadAsync<RouteDefinition>(context).ConfigureAwait(false);
        if (route is null) return;
        if (!string.Equals(id, route.Id, StringComparison.OrdinalIgnoreCase))
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, "Route id in the request body must match the route id.").ConfigureAwait(false);
            return;
        }
        if (await store.GetAsync(id, context.RequestAborted).ConfigureAwait(false) is null)
        {
            await NotFoundAsync(context, "Route", id).ConfigureAwait(false);
            return;
        }
        await store.UpsertAsync(route, context.RequestAborted).ConfigureAwait(false);
        await JsonAsync(context, StatusCodes.Status200OK, route).ConfigureAwait(false);
    }

    public static async Task DeleteRouteAsync(HttpContext context, string id, IRouteStore store, string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;
        await store.DeleteAsync(id, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context)
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(Json, context.RequestAborted).ConfigureAwait(false);
            if (value is not null) return value;
        }
        catch (JsonException) { }
        catch (BadHttpRequestException) { }
        await ErrorAsync(context, StatusCodes.Status400BadRequest, "Request body must be valid JSON.").ConfigureAwait(false);
        return default;
    }

    private static Task NotFoundAsync(HttpContext context, string kind, string id) =>
        ErrorAsync(context, StatusCodes.Status404NotFound, $"{kind} '{id}' was not found.");

    private static Task ErrorAsync(HttpContext context, int status, string message) =>
        JsonAsync(context, status, new { error = new { message, type = "invalid_request_error" } });

    private static async Task JsonAsync(HttpContext context, int status, object value)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(value, Json), context.RequestAborted).ConfigureAwait(false);
    }
}
