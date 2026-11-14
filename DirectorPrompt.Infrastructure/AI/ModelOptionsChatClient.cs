using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Responses;
using Serilog;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Type = System.Type;

#pragma warning disable OPENAI001

namespace DirectorPrompt.Infrastructure.AI;

public sealed partial class ModelOptionsChatClient
(
    IChatClient    inner,
    ModelConfig    modelConfig,
    ProviderConfig providerConfig
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
        options         =   options?.Clone() ?? new ChatOptions();
        options.ModelId ??= modelConfig.ModelName;
        options.Reasoning = options.Reasoning is { } reasoningOptions ?
                                new ReasoningOptions { Effort = reasoningOptions.Effort, Output = reasoningOptions.Output } :
                                null;
        options.AdditionalProperties = options.AdditionalProperties?.Clone();

        if (extraParameters is not null)
        {
            options.AdditionalProperties ??= [];

            foreach (var (key, value) in extraParameters)
                options.AdditionalProperties[key] = value;
        }

        var effort = modelConfig.ReasoningEffort?.Trim().ToLowerInvariant();
        effort = effort is "extra_high" or "extrahigh" ?
                     "xhigh" :
                     effort;
        var mapped = effort switch
        {
            "none"                                          => (ReasoningEffort?)ReasoningEffort.None,
            "minimal" or "low"                              => ReasoningEffort.Low,
            "medium"                                        => ReasoningEffort.Medium,
            "high"                                          => ReasoningEffort.High,
            "xhigh" or "extra_high" or "extrahigh" or "max" => ReasoningEffort.ExtraHigh,
            _                                               => null
        };

        if (mapped is { } effortValue)
        {
            options.Reasoning        ??= new ReasoningOptions();
            options.Reasoning.Effort =   effortValue;
        }

        var parameters = extraParameters is null ?
                             new JsonObject() :
                             JsonSerializer.SerializeToNode(extraParameters)!.AsObject();
        var provider = providerConfig.Provider.ToLowerInvariant();
        var model    = modelConfig.ModelName.ToLowerInvariant();
        var endpoint = Uri.TryCreate(providerConfig.Endpoint, UriKind.Absolute, out var uri) ?
                           uri.Host :
                           string.Empty;

        if (provider == "anthropic")
        {
            ApplyAnthropicOptions(options, parameters, effort, model);
        }
        else
        {
            var reasoningModel = ReasoningModelRegex().IsMatch(model);

            if (reasoningModel && options.Reasoning?.Effort != ReasoningEffort.None)
                options.Temperature = null;

            if (provider == "openai-responses")
            {
                if (mapped is not null || reasoningModel)
                {
                    options.Reasoning        ??= new ReasoningOptions();
                    options.Reasoning.Output ??= ReasoningOutput.Summary;
                    var reasoning = parameters["reasoning"] as JsonObject ?? new JsonObject();

                    if (options.Reasoning.Effort != ReasoningEffort.None)
                        reasoning["summary"] ??= "auto";

                    if (!string.IsNullOrEmpty(effort))
                        reasoning["effort"] ??= effort;

                    parameters["reasoning"] = reasoning.DeepClone();
                }
            }
            else
            {
                ApplyCompatibleOptions(options, parameters, effort, model, endpoint);
            }

            var originalFactory = options.RawRepresentationFactory;
            options.RawRepresentationFactory = client =>
            {
                var raw = originalFactory?.Invoke(client);
                var data = raw switch
                {
                    ChatCompletionOptions chat     => JsonNode.Parse(ModelReaderWriter.Write(chat))!.AsObject(),
                    CreateResponseOptions response => JsonNode.Parse(ModelReaderWriter.Write(response))!.AsObject(),
                    _                              => new JsonObject()
                };

                foreach (var (key, value) in parameters)
                    data[key] = value?.DeepClone();

                var json = BinaryData.FromString(data.ToJsonString());

                return provider == "openai-responses" ?
                           ModelReaderWriter.Read<CreateResponseOptions>(json) :
                           ModelReaderWriter.Read<ChatCompletionOptions>(json);
            };
        }

        return options;
    }

    private static void ApplyCompatibleOptions(ChatOptions options, JsonObject parameters, string? effort, string model, string endpoint)
    {
        var openRouter = endpoint.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase);

        if (model.Contains("gemini", StringComparison.Ordinal) && !openRouter)
        {
            var extraBody = parameters["extra_body"] as JsonObject  ?? new JsonObject();
            var google    = extraBody["google"] as JsonObject       ?? new JsonObject();
            var thinking  = google["thinking_config"] as JsonObject ?? new JsonObject();
            thinking["include_thoughts"] ??= true;
            google["thinking_config"]    =   thinking.DeepClone();
            extraBody["google"]          =   google.DeepClone();
            parameters["extra_body"]     =   extraBody.DeepClone();

            if (thinking["thinking_level"] is not null || thinking["thinking_budget"] is not null)
            {
                options.Reasoning = null;
                return;
            }
        }

        if (string.IsNullOrEmpty(effort))
            return;

        if (openRouter)
        {
            var reasoning = parameters["reasoning"] as JsonObject ?? new JsonObject();
            reasoning["effort"]     ??= effort;
            reasoning["exclude"]    ??= false;
            parameters["reasoning"] =   reasoning.DeepClone();
            options.Reasoning       =   null;
        }
        else if (model.Contains("qwen", StringComparison.Ordinal) || model.Contains("qwq", StringComparison.Ordinal))
        {
            parameters["enable_thinking"] ??= effort != "none";
            options.Reasoning             =   null;
        }
        else if (model.Contains
                     ("deepseek", StringComparison.Ordinal)       ||
                 model.Contains("kimi", StringComparison.Ordinal) ||
                 model.Contains("glm",  StringComparison.Ordinal))
        {
            parameters["thinking"] ??= new JsonObject
            {
                ["type"] = effort == "none" ?
                               "disabled" :
                               "enabled"
            };
            options.Reasoning = null;
        }
        else
        {
            parameters["reasoning_effort"] ??= effort;
        }
    }

    private static void ApplyAnthropicOptions(ChatOptions options, JsonObject parameters, string? effort, string model)
    {
        var version  = ClaudeVersionRegex().Match(model);
        var adaptive = version.Success && new Version(int.Parse(version.Groups[1].Value), int.Parse(version.Groups[2].Value)) >= new Version(4, 6);
        var budget = options.Reasoning?.Effort switch
        {
            ReasoningEffort.Low       => 1024,
            ReasoningEffort.Medium    => 8192,
            ReasoningEffort.High      => 16384,
            ReasoningEffort.ExtraHigh => 32768,
            _                         => 0
        };

        if (parameters["thinking"] is null && !string.IsNullOrEmpty(effort))
        {
            if (effort == "none")
                parameters["thinking"] = new JsonObject { ["type"] = "disabled" };
            else if (adaptive)
                parameters["thinking"] = new JsonObject { ["type"] = "adaptive" };
            else if (budget > 0)
                parameters["thinking"] = new JsonObject { ["type"] = "enabled", ["budget_tokens"] = budget };
        }

        if (adaptive && budget > 0)
        {
            var output = parameters["output_config"] as JsonObject ?? new JsonObject();
            output["effort"] ??= effort is "extra_high" or "extrahigh" ?
                                     "xhigh" :
                                     effort;
            parameters["output_config"] = output.DeepClone();
        }

        var thinkingType = parameters["thinking"]?["type"]?.GetValue<string>();

        if (thinkingType is "enabled" or "adaptive")
        {
            options.Temperature = null;

            if (options.ToolMode is RequiredChatToolMode { RequiredFunctionName: null })
                options.ToolMode = ChatToolMode.Auto;
        }

        parameters["model"]    ??= options.ModelId ?? model;
        parameters["messages"] ??= new JsonArray();
        var requestedBudget = parameters["thinking"]?["budget_tokens"]?.GetValue<int>() ?? 0;
        parameters["max_tokens"] ??= options.MaxOutputTokens ?? Math.Max(8192, requestedBudget + 8192);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parameters.ToJsonString())!;
        options.RawRepresentationFactory = _ => MessageCreateParams.FromRawUnchecked
            (new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), body);
    }

    [GeneratedRegex(@"^(?:o\d|gpt-(?:[5-9]|[1-9]\d))")]
    private static partial Regex ReasoningModelRegex();

    [GeneratedRegex(@"claude-(?:opus|sonnet)-(\d+)[.-](\d+)")]
    private static partial Regex ClaudeVersionRegex();

    private static Dictionary<string, JsonElement>? ParseExtraParameters(string? value)
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
