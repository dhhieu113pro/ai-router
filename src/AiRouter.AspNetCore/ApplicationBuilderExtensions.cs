using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

public static class AiRouterApplicationBuilderExtensions
{
    public static IEndpointRouteBuilder UseAiRouter(
        this IEndpointRouteBuilder endpoints,
        string? prefix = null,
        string? bearerKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var normalizedPrefix = prefix?.Trim().Trim('/') ?? string.Empty;
        if (normalizedPrefix.Length == 0)
            return endpoints.MapAiRouterOpenAiEndpoints(bearerKey);

        endpoints.MapGroup($"/{normalizedPrefix}")
            .MapAiRouterOpenAiEndpoints(bearerKey);
        return endpoints;
    }
}
