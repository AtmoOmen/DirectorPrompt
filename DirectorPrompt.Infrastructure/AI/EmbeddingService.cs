using System.ClientModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class EmbeddingService
(
    string  provider,
    string  endpoint,
    string? apiKey,
    string  modelName,
    string? customHeaders = null
)
    : IEmbeddingService
{
    public EmbeddingService(ResolvedEmbeddingConfig config) : this(config.Provider, config.Endpoint, config.APIKey, config.ModelName, config.CustomHeaders)
    {
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var generator  = CreateGenerator();
        var embeddings = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
        return embeddings[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync
    (
        IReadOnlyList<string> texts,
        CancellationToken     cancellationToken = default
    )
    {
        if (texts.Count == 0)
            return [];

        var generator  = CreateGenerator();
        var embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);

        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateGenerator()
    {
        var normalizedProvider = provider.ToLowerInvariant();

        var effectiveEndpoint = normalizedProvider switch
        {
            "ollama" => string.IsNullOrWhiteSpace(endpoint) ?
                            "http://localhost:11434/v1" :
                            endpoint,
            _ => endpoint
        };

        var openAIClient = normalizedProvider switch
        {
            "openai" or "ollama" or "custom" => CreateOpenAIClient(effectiveEndpoint),
            _                                => throw new ArgumentException($"不支持的 Embedding Provider: {provider}")
        };

        var embeddingClient = openAIClient.GetEmbeddingClient(modelName);

        return embeddingClient.AsIEmbeddingGenerator();
    }

    private OpenAIClient CreateOpenAIClient(string endPoint)
    {
        OpenAIClientOptions options = new();

        if (!string.IsNullOrWhiteSpace(endPoint))
            options.Endpoint = new Uri(endPoint);

        CustomHeaderPipelinePolicy.ApplyToOptions(options, customHeaders);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return new OpenAIClient
            (
                new ApiKeyCredential(apiKey),
                options
            );
        }

        throw new ArgumentException("APIKey 不能为空");
    }
}

public sealed class EmbeddingServiceFactory : IEmbeddingServiceFactory
{
    public IEmbeddingService Create(ResolvedEmbeddingConfig config) => new EmbeddingService(config);
}
