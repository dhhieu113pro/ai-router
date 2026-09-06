namespace AiRouter.Routing;

public sealed record RouterRequestContext(
    string? AffinityKey = null,
    string AffinitySource = "route",
    string? RequestId = null);
