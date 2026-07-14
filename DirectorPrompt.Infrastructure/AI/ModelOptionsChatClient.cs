using System.Runtime.CompilerServices;
using System.Text.Json;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ModelOptionsChatClient
(
    IChatClient inner,
    ModelConfig modelConfig
) : IChatClient
{
    private readonly IReadOnlyDictionary<string, JsonElement>? extraParameters =
        ParseExtraParameters(modelConfig.ExtraParameters);

    public async Task<ChatResponse> GetResponseAsync
    (
        IEnumerable<ChatMessage> messages,
        ChatOptions?             options           = null,
        CancellationToken        cancellationToken = default
    )
    {
        options = ApplyModelOptions(options);

        return await inner.GetResponseAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync
    (
        IEnumerable<ChatMessage>                   messages,
        ChatOptions?                               options           = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        options = ApplyModelOptions(options);

        await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() =>
        inner.Dispose();

    private ChatOptions ApplyModelOptions(ChatOptions? options)
    {
        options ??= new ChatOptions();

        if (extraParameters is not null)
        {
            options.AdditionalProperties ??= [];

            foreach (var (key, value) in extraParameters)
                options.AdditionalProperties[key] = value;
        }

        if (string.IsNullOrWhiteSpace(modelConfig.ReasoningEffort))
            return options;

        var effort = modelConfig.ReasoningEffort.Trim().ToLowerInvariant();
        var mapped = effort switch
        {
            "none"                                 => (ReasoningEffort?)ReasoningEffort.None,
            "low"                                  => ReasoningEffort.Low,
            "medium"                               => ReasoningEffort.Medium,
            "high"                                 => ReasoningEffort.High,
            "xhigh" or "extra_high" or "extrahigh" => ReasoningEffort.ExtraHigh,
            _                                      => null
        };

        if (mapped is { } effortValue)
        {
            options.Reasoning        ??= new ReasoningOptions();
            options.Reasoning.Effort =   effortValue;
        }
        else
        {
            options.AdditionalProperties                     ??= [];
            options.AdditionalProperties["reasoning_effort"] =   effort;
        }

        return options;
    }

    private static IReadOnlyDictionary<string, JsonElement>? ParseExtraParameters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value);
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "解析模型自定义参数失败");
            return null;
        }
    }
}
