using System.Text.Json;
using AiRouter.AspNetCore;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class AiRouterConfigurationManagementEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAiRouterConfigurationManagementEndpoints(
        this IEndpointRouteBuilder endpoints,
        string? bearerKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/config/export", (HttpContext context, IProviderManager providers, IRouteStore routes) =>
            ConfigurationManagementHandlers.ExportAsync(context, providers, routes, bearerKey));
        endpoints.MapPost("/config/import", (HttpContext context, IProviderManager providers, IRouteStore routes) =>
            ConfigurationManagementHandlers.ImportAsync(context, providers, routes, bearerKey));

        return endpoints;
    }
}

internal static class ConfigurationManagementHandlers
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task ExportAsync(
        HttpContext context,
        IProviderManager providerManager,
        IRouteStore routeStore,
        string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;

        var includeSecrets = bool.TryParse(context.Request.Query["includeSecrets"].ToString(), out var parsed) && parsed;
        var providers = await providerManager.ListAsync(context.RequestAborted).ConfigureAwait(false);
        var routes = await routeStore.ListAsync(context.RequestAborted).ConfigureAwait(false);
        var exportedProviders = includeSecrets
            ? providers
            : providers.Select(static provider => provider.Redacted()).ToArray();

        await JsonAsync(
            context,
            StatusCodes.Status200OK,
            new AiRouterConfigurationDocument(SchemaVersion, exportedProviders, routes)).ConfigureAwait(false);
    }

    public static async Task ImportAsync(
        HttpContext context,
        IProviderManager providerManager,
        IRouteStore routeStore,
        string? key)
    {
        if (!await BearerKeyAuthorizer.RequireAsync(context, key).ConfigureAwait(false)) return;

        AiRouterConfigurationDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<AiRouterConfigurationDocument>(
                context.Request.Body,
                Json,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, "Request body must be valid JSON.").ConfigureAwait(false);
            return;
        }

        if (document is null)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, "Request body must be valid JSON.").ConfigureAwait(false);
            return;
        }

        if (document.SchemaVersion != SchemaVersion)
        {
            await ErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                $"Unsupported configuration schemaVersion '{document.SchemaVersion}'. Expected {SchemaVersion}.").ConfigureAwait(false);
            return;
        }

        var importedProviders = document.Providers ?? [];
        var importedRoutes = document.Routes ?? [];

        var mode = context.Request.Query["mode"].ToString().Trim().ToLowerInvariant();
        mode = string.IsNullOrEmpty(mode) ? "merge" : mode;
        if (mode is not ("merge" or "replace"))
        {
            await ErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Import mode must be 'merge' or 'replace'.").ConfigureAwait(false);
            return;
        }

        try
        {
            var existingProviders = await providerManager.ListAsync(context.RequestAborted).ConfigureAwait(false);
            var existingRoutes = await routeStore.ListAsync(context.RequestAborted).ConfigureAwait(false);

            foreach (var provider in importedProviders)
            {
                if (await providerManager.GetAsync(provider.Id, context.RequestAborted).ConfigureAwait(false) is null)
                    await providerManager.AddAsync(provider, context.RequestAborted).ConfigureAwait(false);
                else
                    await providerManager.UpdateAsync(provider.Id, provider, context.RequestAborted).ConfigureAwait(false);
            }

            foreach (var route in importedRoutes)
                await routeStore.UpsertAsync(route, context.RequestAborted).ConfigureAwait(false);

            var providersDeleted = 0;
            var routesDeleted = 0;
            if (mode == "replace")
            {
                var wantedRouteIds = importedRoutes.Select(static route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var route in existingRoutes.Where(route => !wantedRouteIds.Contains(route.Id)))
                {
                    await routeStore.DeleteAsync(route.Id, context.RequestAborted).ConfigureAwait(false);
                    routesDeleted++;
                }

                var wantedProviderIds = importedProviders.Select(static provider => provider.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var provider in existingProviders.Where(provider => !wantedProviderIds.Contains(provider.Id)))
                {
                    await providerManager.DeleteAsync(provider.Id, context.RequestAborted).ConfigureAwait(false);
                    providersDeleted++;
                }
            }

            await JsonAsync(
                context,
                StatusCodes.Status200OK,
                new
                {
                    mode,
                    providersUpserted = importedProviders.Count,
                    providersDeleted,
                    routesUpserted = importedRoutes.Count,
                    routesDeleted
                }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message).ConfigureAwait(false);
        }
    }

    private static Task ErrorAsync(HttpContext context, int status, string message) =>
        JsonAsync(context, status, new { error = new { message, type = "invalid_request_error" } });

    private static async Task JsonAsync(HttpContext context, int status, object value)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(value, Json), context.RequestAborted).ConfigureAwait(false);
    }

    internal sealed record AiRouterConfigurationDocument(
        int SchemaVersion,
        IReadOnlyList<ProviderDefinition>? Providers,
        IReadOnlyList<RouteDefinition>? Routes);
}
