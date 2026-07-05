using System.ClientModel;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ChatClientFactory : IChatClientFactory
{
    public IChatClient Create(ModelConfig config)
    {
        var provider = config.Provider.ToLowerInvariant();

        var openAIClient = provider switch
        {
            "openai" => CreateOpenAIClient(config),
            _        => throw new ArgumentException($"不支持的 Provider: {config.Provider}")
        };

        var chatClient = openAIClient.GetChatClient(config.ModelName);

        return chatClient.AsIChatClient();
    }

    private static OpenAIClient CreateOpenAIClient(ModelConfig config)
    {
        OpenAIClientOptions options = new();

        if (!string.IsNullOrWhiteSpace(config.Endpoint))
            options.Endpoint = new Uri(config.Endpoint);

        if (!string.IsNullOrWhiteSpace(config.APIKey))
        {
            return new OpenAIClient
            (
                new ApiKeyCredential(config.APIKey),
                options
            );
        }

        throw new ArgumentException("APIKey 不能为空");
    }
}
