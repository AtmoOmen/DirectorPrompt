using System.ClientModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class EmbeddingService : IEmbeddingService
{
    private readonly string  provider;
    private readonly string  endpoint;
    private readonly string? apiKey;
    private readonly string  modelName;

    public EmbeddingService(string provider, string endpoint, string? apiKey, string modelName)
    {
        this.provider  = provider;
        this.endpoint  = endpoint;
        this.apiKey    = apiKey;
        this.modelName = modelName;
    }

    public EmbeddingService(ResolvedEmbeddingConfig config) : this(config.Provider, config.Endpoint, config.APIKey, config.ModelName)
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
        var generator  = CreateGenerator();
        var embeddings = new List<float[]>(texts.Count);

        foreach (var text in texts)
        {
            var result = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
            embeddings.Add(result[0].Vector.ToArray());
        }

        return embeddings;
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

    private OpenAIClient CreateOpenAIClient(string endpoint)
    {
        OpenAIClientOptions options = new();

        if (!string.IsNullOrWhiteSpace(endpoint))
            options.Endpoint = new Uri(endpoint);

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
