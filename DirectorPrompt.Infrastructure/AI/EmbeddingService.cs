using System.ClientModel;
using System.Diagnostics;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using OpenAI;
using Serilog;

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
        var stopwatch = Stopwatch.StartNew();

        Log.Debug("开始生成向量: 文本长度={TextLength}", text.Length);

        try
        {
            var embeddings = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
            var vector     = embeddings[0].Vector.ToArray();

            Log.Debug
            (
                "向量生成完成: 文本长度={TextLength}, 维度={Dimension}, 耗时={ElapsedMilliseconds}ms",
                text.Length,
                vector.Length,
                stopwatch.ElapsedMilliseconds
            );

            return vector;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug("向量生成已取消: 文本长度={TextLength}, 耗时={ElapsedMilliseconds}ms", text.Length, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "向量生成失败: 文本长度={TextLength}, 耗时={ElapsedMilliseconds}ms", text.Length, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync
    (
        IReadOnlyList<string> texts,
        CancellationToken     cancellationToken = default
    )
    {
        if (texts.Count == 0)
            return [];

        var stopwatch       = Stopwatch.StartNew();
        var totalTextLength = texts.Sum(text => text.Length);
        var result = new float[texts.Count][];

        const int BATCH_SIZE = 10;

        Log.Debug
        (
            "开始批量生成向量: 文本数={TextCount}, 总文本长度={TotalTextLength}, 批次大小={BatchSize}",
            texts.Count,
            totalTextLength,
            BATCH_SIZE
        );

        try
        {
            for (var i = 0; i < texts.Count; i += BATCH_SIZE)
            {
                var count = Math.Min(BATCH_SIZE, texts.Count - i);
                var batch = texts.Skip(i).Take(count).ToArray();

                var embeddings = await generator.GenerateAsync(batch, cancellationToken: cancellationToken);

                for (var j = 0; j < embeddings.Count; j++)
                    result[i + j] = embeddings[j].Vector.ToArray();
            }

            var dimension = result.FirstOrDefault(vector => vector is not null)?.Length ?? 0;

            Log.Debug
            (
                "批量向量生成完成: 文本数={TextCount}, 维度={Dimension}, 耗时={ElapsedMilliseconds}ms",
                texts.Count,
                dimension,
                stopwatch.ElapsedMilliseconds
            );

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug("批量向量生成已取消: 文本数={TextCount}, 耗时={ElapsedMilliseconds}ms", texts.Count, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "批量向量生成失败: 文本数={TextCount}, 耗时={ElapsedMilliseconds}ms", texts.Count, stopwatch.ElapsedMilliseconds);
            throw;
        }
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

        Log.Information("创建 Embedding 客户端: 提供商={Provider}, 模型={Model}, Endpoint={Endpoint}", normalizedProvider, modelName, endpoint);

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
