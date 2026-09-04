using System.Text.Json;
using AiRouter.Configuration;
using AiRouter.Providers;

namespace AiRouter.Routing;

public sealed class AiRouterService : IAiRouter
{
    public AiRouterService(RouteResolver resolver, IProviderManager providers, AiRouterOptions? options = null)
    {
    }

    public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
