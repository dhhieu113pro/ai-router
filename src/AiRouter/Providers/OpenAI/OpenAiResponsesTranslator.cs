using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using AiRouter.Models;

namespace AiRouter.Providers.OpenAI;

public sealed class ResponsesTranslationException(string message) : Exception(message);

public sealed class OpenAiResponsesTranslator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChatCompletionRequest ToChatRequest(ResponsesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AdditionalProperties.Count > 0)
        {
            var unsupported = request.AdditionalProperties.Keys.Order(StringComparer.Ordinal).First();
            throw new ResponsesTranslationException($"Responses feature '{unsupported}' is not supported by the chat-completions compatibility mode.");
        }

        var result = new ChatCompletionRequest
        {
            Model = request.Model,
            Stream = request.Stream,
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxTokens = request.MaxOutputTokens,
            ToolChoice = request.ToolChoice?.Clone()
        };

        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            result.Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = JsonSerializer.SerializeToElement(request.Instructions, Json)
            });
        }

        AppendInputMessages(request.Input, result.Messages);
        result.Tools = TranslateTools(request.Tools);

        return result;
    }

    public JsonElement ToResponsesResponse(JsonElement chatResponse, string model)
    {
        var id = chatResponse.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : $"resp_{Guid.NewGuid():N}";

        var actualModel = chatResponse.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString()
            : model;

        var text = ExtractAssistantText(chatResponse);
        var usage = TranslateUsage(chatResponse);

        return JsonSerializer.SerializeToElement(new
        {
            id,
            @object = "response",
            status = "completed",
            model = actualModel,
            output = new[]
            {
                new
                {
                    id = $"msg_{Guid.NewGuid():N}",
                    type = "message",
                    role = "assistant",
                    status = "completed",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text
                        }
                    }
                }
            },
            usage
        }, Json);
    }

    public Stream ToResponsesStream(Stream chatSse, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chatSse);

        var pipe = new Pipe();
        _ = PumpSseAsync(chatSse, pipe.Writer, ct);
        return pipe.Reader.AsStream();
    }

    private static void AppendInputMessages(JsonElement input, ICollection<ChatMessage> messages)
    {
        switch (input.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = input.Clone()
                });
                return;

            case JsonValueKind.Array:
                foreach (var item in input.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String ||
                        !item.TryGetProperty("content", out var content))
                    {
                        throw new ResponsesTranslationException("Structured Responses input must contain objects with string 'role' and 'content'.");
                    }

                    messages.Add(new ChatMessage
                    {
                        Role = role.GetString()!,
                        Content = content.Clone()
                    });
                }
                return;

            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                throw new ResponsesTranslationException("Responses input is required.");

            default:
                throw new ResponsesTranslationException("Responses input must be a string or an array of message objects in compatibility mode.");
        }
    }

    private static JsonElement? TranslateTools(JsonElement? tools)
    {
        if (tools is null || tools.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (tools.Value.ValueKind != JsonValueKind.Array)
            throw new ResponsesTranslationException("Responses tools must be an array.");

        var translated = new List<object>();
        foreach (var tool in tools.Value.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object ||
                !tool.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "function", StringComparison.Ordinal))
            {
                throw new ResponsesTranslationException("Only function tools are supported by the chat-completions compatibility mode.");
            }

            if (!tool.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                throw new ResponsesTranslationException("Function tools require a name.");

            var description = tool.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString()
                : null;
            object? parameters = tool.TryGetProperty("parameters", out var parametersElement)
                ? JsonSerializer.Deserialize<object>(parametersElement.GetRawText(), Json)
                : null;

            translated.Add(new
            {
                type = "function",
                function = new
                {
                    name = name.GetString(),
                    description,
                    parameters
                }
            });
        }

        return JsonSerializer.SerializeToElement(translated, Json);
    }

    private static string ExtractAssistantText(JsonElement chatResponse)
    {
        if (!chatResponse.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                continue;
            if (!message.TryGetProperty("content", out var content))
                continue;

            return content.ValueKind == JsonValueKind.String ? content.GetString()! : content.GetRawText();
        }

        return string.Empty;
    }

    private static object? TranslateUsage(JsonElement chatResponse)
    {
        if (!chatResponse.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        int? Read(string name) =>
            usage.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number)
                ? number
                : null;

        return new
        {
            input_tokens = Read("prompt_tokens"),
            output_tokens = Read("completion_tokens"),
            total_tokens = Read("total_tokens")
        };
    }

    private static async Task PumpSseAsync(Stream source, PipeWriter writer, CancellationToken ct)
    {
        Exception? failure = null;
        try
        {
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    break;
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var payload = line[5..].TrimStart();
                if (payload.Length == 0)
                    continue;

                if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
                {
                    await WriteEventAsync(writer, new
                    {
                        type = "response.completed",
                        response = new { status = "completed" }
                    }, ct).ConfigureAwait(false);
                    break;
                }

                using var chunk = JsonDocument.Parse(payload);
                var delta = ExtractDelta(chunk.RootElement);
                if (delta is null)
                    continue;

                await WriteEventAsync(writer, new
                {
                    type = "response.output_text.delta",
                    delta
                }, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
            await writer.CompleteAsync(failure).ConfigureAwait(false);
        }
    }

    private static string? ExtractDelta(JsonElement chunk)
    {
        if (!chunk.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("delta", out var deltaObject) || deltaObject.ValueKind != JsonValueKind.Object)
                continue;
            if (!deltaObject.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                continue;

            return content.GetString();
        }

        return null;
    }

    private static async Task WriteEventAsync(PipeWriter writer, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        var bytes = Encoding.UTF8.GetBytes($"event: {JsonSerializer.SerializeToElement(payload, Json).GetProperty("type").GetString()}\ndata: {json}\n\n");
        await writer.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}
