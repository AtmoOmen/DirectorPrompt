using System.ClientModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;

    public EmbeddingService
    (
        string  provider,
        string  endpoint,
        string? apiKey,
        string  modelName,
        string? customHeaders = null
    ) =>
        generator = CreateGenerator(provider, endpoint, apiKey, modelName, customHeaders);

    public EmbeddingService(ResolvedEmbeddingConfig config) : this(config.Provider, config.Endpoint, config.APIKey, config.ModelName, config.CustomHeaders)
    {
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
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

        var result = new float[texts.Count][];

        const int BATCH_SIZE = 10;

        for (var i = 0; i < texts.Count; i += BATCH_SIZE)
        {
            var count = Math.Min(BATCH_SIZE, texts.Count - i);
            var batch = texts.Skip(i).Take(count).ToArray();

            var embeddings = await generator.GenerateAsync(batch, cancellationToken: cancellationToken);

            for (var j = 0; j < embeddings.Count; j++)
                result[i + j] = embeddings[j].Vector.ToArray();
        }

        return result;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateGenerator
    (
        string  provider,
        string  endpoint,
        string? apiKey,
        string  modelName,
        string? customHeaders
    )
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
            "openai" or "ollama" or "custom" => CreateOpenAIClient(effectiveEndpoint, apiKey, customHeaders),
            _                                => throw new ArgumentException($"不支持的 Embedding Provider: {provider}")
        };

        var embeddingClient = openAIClient.GetEmbeddingClient(modelName);

        return embeddingClient.AsIEmbeddingGenerator();
    }

    private static OpenAIClient CreateOpenAIClient(string endPoint, string? apiKey, string? customHeaders)
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
