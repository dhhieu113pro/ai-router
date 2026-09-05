using AiRouter.Providers;

namespace AiRouter.Providers.OpenAI;

internal static class OpenAiErrorClassifier
{
    public static ProviderFailureKind Classify(int statusCode) => statusCode switch
    {
        400 or 422 => ProviderFailureKind.InvalidRequest,
        404 => ProviderFailureKind.TargetFailure,
        429 => ProviderFailureKind.RateLimited,
        401 or 403 or 408 or 409 => ProviderFailureKind.ProviderFailure,
        >= 500 => ProviderFailureKind.ProviderFailure,
        _ => ProviderFailureKind.TargetFailure
    };
}
