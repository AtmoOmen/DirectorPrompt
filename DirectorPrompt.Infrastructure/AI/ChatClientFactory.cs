using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using OpenAI;
using Serilog;
using ReasoningEffort = DirectorPrompt.Domain.Enums.ReasoningEffort;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ChatClientFactory : IChatClientFactory
{
    public IChatClient Create(ProviderConfig provider, ModelConfig model)
    {
        var providerType = provider.Provider.ToLowerInvariant();

        Log.Information
        (
            "创建 ChatClient: Provider={Provider}, 模型={Model}, Endpoint={Endpoint}",
            providerType,
            model.ModelName,
            provider.Endpoint
        );

        var inner = providerType switch
        {
            "anthropic" => CreateAnthropicClient(provider, model),
            _           => CreateOpenAICompatibleClient(provider, model, providerType)
        };

        var wrapped = new ModelOptionsChatClient(inner, model);

        return new ChatClientBuilder(wrapped)
               .UseFunctionInvocation()
               .Build();
    }

    private static IChatClient CreateOpenAICompatibleClient(ProviderConfig provider, ModelConfig model, string providerType)
    {
        OpenAIClientOptions options = new();

        var endpoint = providerType switch
        {
            "ollama" => string.IsNullOrWhiteSpace(provider.Endpoint) ?
                            "http://localhost:11434/v1" :
                            provider.Endpoint,
            _ => provider.Endpoint
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
            options.Endpoint = new Uri(endpoint);

        var apiKey = !string.IsNullOrWhiteSpace(provider.APIKey) ?
                         provider.APIKey :
                         providerType switch
                         {
                             "openai" => throw new ArgumentException("OpenAI Provider 需要 API Key"),
                             _        => "dummy-key"
                         };

        var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        var chatClient   = openAIClient.GetChatClient(model.ModelName);

        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAnthropicClient(ProviderConfig provider, ModelConfig model)
    {
        if (string.IsNullOrWhiteSpace(provider.APIKey))
            throw new ArgumentException("Anthropic Provider 需要 API Key");

        return new AnthropicChatClient
        (
            provider.APIKey,
            model.ModelName,
            provider.Endpoint
        );
    }
}

public sealed class ModelOptionsChatClient
(
    IChatClient inner,
    ModelConfig modelConfig
) : IChatClient
{
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

        if (!string.IsNullOrWhiteSpace(modelConfig.ExtraParameters))
        {
            try
            {
                var extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modelConfig.ExtraParameters);

                if (extra is not null)
                {
                    options.AdditionalProperties ??= [];

                    foreach (var (key, value) in extra)
                        options.AdditionalProperties[key] = value;
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "解析模型自定义参数失败: {ExtraParameters}", modelConfig.ExtraParameters);
            }
        }

        if (modelConfig.ReasoningEffort != ReasoningEffort.None)
        {
            options.AdditionalProperties                     ??= [];
            options.AdditionalProperties["reasoning_effort"] =   modelConfig.ReasoningEffort.ToString().ToLowerInvariant();
        }

        return options;
    }
}
