using System.ClientModel;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using OpenAI;
using Serilog;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ChatClientFactory : IChatClientFactory
{
    public IChatClient Create(ModelConfig config)
    {
        var provider = config.Provider.ToLowerInvariant();

        Log.Information
        (
            "创建 ChatClient: Provider={Provider}, 模型={Model}, Endpoint={Endpoint}",
            provider,
            config.ModelName,
            config.Endpoint
        );

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
