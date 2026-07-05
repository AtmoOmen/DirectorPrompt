using System.ClientModel;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ModelConnectionTester : IModelConnectionTester
{
    public async Task TestChatAsync(string provider, string endpoint, string? apiKey, string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("模型名不能为空");
        }

        var client    = CreateOpenAIClient(provider, endpoint, apiKey);
        var chatClient = client.GetChatClient(modelName).AsIChatClient();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "ping")
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        if (response.Messages.Count == 0)
        {
            throw new InvalidOperationException("模型返回了空响应");
        }
    }

    public async Task TestEmbeddingAsync(string provider, string endpoint, string? apiKey, string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("模型名不能为空");
        }

        var client           = CreateOpenAIClient(provider, endpoint, apiKey);
        var embeddingClient  = client.GetEmbeddingClient(modelName);
        var generator        = embeddingClient.AsIEmbeddingGenerator();

        var result = await generator.GenerateAsync(["test"], cancellationToken: cancellationToken);

        if (result.Count == 0 || result[0].Vector.Length == 0)
        {
            throw new InvalidOperationException("Embedding 模型返回了空向量");
        }
    }

    private static OpenAIClient CreateOpenAIClient(string provider, string endpoint, string? apiKey)
    {
        var normalizedProvider = provider.ToLowerInvariant();

        var options = new OpenAIClientOptions();

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.Endpoint = new Uri(endpoint);
        }

        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : normalizedProvider switch
            {
                "openai" => throw new ArgumentException("OpenAI Provider 需要 API Key"),
                _        => "dummy-key"
            };

        return new OpenAIClient
        (
            new ApiKeyCredential(effectiveKey),
            options
        );
    }
}
