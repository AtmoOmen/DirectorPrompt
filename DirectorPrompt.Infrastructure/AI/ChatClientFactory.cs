using System.ClientModel;
using System.Collections.Immutable;
using Anthropic;
using Anthropic.Core;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using OpenAI;
using Serilog;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ChatClientFactory : IChatClientFactory, IDisposable
{
    private ImmutableDictionary<string, IChatClient> clients = ImmutableDictionary<string, IChatClient>.Empty;

    public IChatClient Create(ProviderConfig provider, ModelConfig model)
    {
        var key = string.Join
        (
            '\0',
            provider.Provider,
            provider.Endpoint,
            provider.APIKey,
            provider.CustomHeaders,
            model.ModelName,
            model.ExtraParameters,
            model.ReasoningEffort
        );

        while (true)
        {
            var snapshot = clients;

            if (snapshot.TryGetValue(key, out var existing))
            {
                Log.Debug
                (
                    "复用 ChatClient: 提供商={Provider}, 模型={Model}, 已缓存客户端数={ClientCount}",
                    provider.Provider,
                    model.ModelName,
                    snapshot.Count
                );
                return existing;
            }

            var created = CreateClient(provider, model);
            var updated = snapshot.Add(key, created);

            if (ReferenceEquals(Interlocked.CompareExchange(ref clients, updated, snapshot), snapshot))
            {
                Log.Information
                (
                    "ChatClient 已加入缓存: 提供商={Provider}, 模型={Model}, 已缓存客户端数={ClientCount}",
                    provider.Provider,
                    model.ModelName,
                    updated.Count
                );
                return created;
            }

            created.Dispose();
        }
    }

    public void Reset()
    {
        var previous = Interlocked.Exchange(ref clients, ImmutableDictionary<string, IChatClient>.Empty);

        if (previous.Count > 0)
            Log.Information("重置 ChatClient 缓存: 已释放客户端数={ClientCount}", previous.Count);

        foreach (var client in previous.Values)
            client.Dispose();
    }

    public void Dispose() =>
        Reset();

    private static IChatClient CreateClient(ProviderConfig provider, ModelConfig model)
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

        var builder = new ChatClientBuilder(wrapped);

        if (providerType == "anthropic")
            builder = builder.Use(next => new CacheControlChatClient(next));

        return builder
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

        CustomHeaderPipelinePolicy.ApplyToOptions(options, provider.CustomHeaders);

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

        var options = new ClientOptions
        {
            ApiKey = provider.APIKey,
            BaseUrl = string.IsNullOrWhiteSpace(provider.Endpoint) ?
                          null :
                          provider.Endpoint
        };

        var customHeaders = CustomHeaderPipelinePolicy.Parse(provider.CustomHeaders);

        if (customHeaders is not null)
            options.ExtraHeaders = customHeaders;

        return new AnthropicClient(options)
            .AsIChatClient(model.ModelName, 8192);
    }
}
