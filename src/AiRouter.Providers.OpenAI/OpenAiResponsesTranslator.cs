using System.Text.Json;
using AiRouter.Models;

namespace AiRouter.Providers.OpenAI;

public sealed class ResponsesTranslationException(string message) : Exception(message);

public sealed class OpenAiResponsesTranslator
{
    public ChatCompletionRequest ToChatRequest(ResponsesRequest request) => throw new NotImplementedException();
    public JsonElement ToResponsesResponse(JsonElement chatResponse, string model) => throw new NotImplementedException();
    public Stream ToResponsesStream(Stream chatSse, CancellationToken ct = default) => throw new NotImplementedException();
}
