using System.ClientModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class ModelConnectionTester : IModelConnectionTester
{
    public async Task<IReadOnlyList<string>> FetchModelsAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        string?           customHeaders     = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedProvider = provider.ToLowerInvariant();

        if (normalizedProvider == "anthropic")
            throw new InvalidOperationException("Anthropic 不支持获取模型列表, 请手动输入模型名");

        var effectiveEndpoint = normalizedProvider switch
        {
            "ollama" => string.IsNullOrWhiteSpace(endpoint) ?
                            "http://localhost:11434/v1/models" :
                            BuildModelsURI(endpoint),
            _ => BuildModelsURI(endpoint)
        };

        using var httpClient = new HttpClient();
        using var request    = new HttpRequestMessage(HttpMethod.Get, effectiveEndpoint);

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        ApplyCustomHeaders(request, customHeaders);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取模型列表失败 ({(int)response.StatusCode}): {content}");

        var models = ParseModelIds(content);

        if (models.Count == 0)
            throw new InvalidOperationException("端点没有返回可用模型");

        return models;
    }

    public async Task TestChatAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        string            modelName,
        string?           customHeaders     = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("模型名不能为空");

        var normalizedProvider = provider.ToLowerInvariant();

        if (normalizedProvider == "anthropic")
        {
            await TestAnthropicChatAsync(apiKey, endpoint, modelName, customHeaders, cancellationToken);
            return;
        }

        var client     = CreateOpenAIClient(normalizedProvider, endpoint, apiKey, customHeaders);
        var chatClient = client.GetChatClient(modelName).AsIChatClient();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "连接测试，回复任一字符即可")
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        if (response.Messages.Count == 0)
            throw new InvalidOperationException("模型返回了空响应");
    }

    public async Task TestEmbeddingAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        string            modelName,
        string?           customHeaders     = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("模型名不能为空");

        var normalizedProvider = provider.ToLowerInvariant();
        var effectiveEndpoint = normalizedProvider switch
        {
            "ollama" => string.IsNullOrWhiteSpace(endpoint) ?
                            "http://localhost:11434/v1" :
                            endpoint,
            _ => endpoint
        };

        var client          = CreateOpenAIClient(normalizedProvider, effectiveEndpoint, apiKey, customHeaders);
        var embeddingClient = client.GetEmbeddingClient(modelName);
        var generator       = embeddingClient.AsIEmbeddingGenerator();

        var result = await generator.GenerateAsync(["test"], cancellationToken: cancellationToken);

        if (result.Count == 0 || result[0].Vector.Length == 0)
            throw new InvalidOperationException("Embedding 模型返回了空向量");
    }

    private static async Task TestAnthropicChatAsync
    (
        string?           apiKey,
        string?           endpoint,
        string            modelName,
        string?           customHeaders,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Anthropic Provider 需要 API Key");

        var options = new ClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = string.IsNullOrWhiteSpace(endpoint) ?
                          null :
                          endpoint
        };

        var parsedHeaders = CustomHeaderPipelinePolicy.Parse(customHeaders);

        if (parsedHeaders is not null)
            options.ExtraHeaders = parsedHeaders;

        using var anthropicClient = new AnthropicClient(options);
        using var chatClient      = anthropicClient.AsIChatClient(modelName, 16);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "连接测试，回复任一字符即可")
        };

        var chatOptions = new ChatOptions { MaxOutputTokens = 16 };

        var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

        if (response.Messages.Count == 0)
            throw new InvalidOperationException("模型返回了空响应");
    }

    private static void ApplyCustomHeaders(HttpRequestMessage request, string? customHeaders)
    {
        var parsed = CustomHeaderPipelinePolicy.Parse(customHeaders);

        if (parsed is null)
            return;

        foreach (var (key, value) in parsed)
        {
            request.Headers.Remove(key);
            request.Headers.Add(key, value);
        }
    }

    private static string BuildModelsURI(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "https://api.openai.com/v1/models";

        var trimmedEndpoint = endpoint.Trim().TrimEnd('/');

        if (trimmedEndpoint.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return trimmedEndpoint;

        if (trimmedEndpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{trimmedEndpoint}/models";

        return $"{trimmedEndpoint}/v1/models";
    }

    private static List<string> ParseModelIds(string content)
    {
        using var document = JsonDocument.Parse(content);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
                   .Select
                   (item => item.TryGetProperty("id", out var id) ?
                                id.GetString() :
                                null
                   )
                   .Where(id => !string.IsNullOrWhiteSpace(id))
                   .Select(id => id!)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .Order(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private static OpenAIClient CreateOpenAIClient(string provider, string? endpoint, string? apiKey, string? customHeaders = null)
    {
        var options = new OpenAIClientOptions();

        if (!string.IsNullOrWhiteSpace(endpoint))
            options.Endpoint = new Uri(endpoint);

        CustomHeaderPipelinePolicy.ApplyToOptions(options, customHeaders);

        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ?
                               apiKey :
                               provider switch
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
