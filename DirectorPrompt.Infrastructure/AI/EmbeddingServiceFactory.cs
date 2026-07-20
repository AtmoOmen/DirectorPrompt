using System.Collections.Immutable;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class EmbeddingServiceFactory : IEmbeddingServiceFactory
{
    private ImmutableDictionary<string, IEmbeddingService> services =
        ImmutableDictionary<string, IEmbeddingService>.Empty;

    public IEmbeddingService Create(ResolvedEmbeddingConfig config)
    {
        var key = string.Join('\0', config.Provider, config.Endpoint, config.APIKey, config.ModelName, config.CustomHeaders);

        while (true)
        {
            var snapshot = services;

            if (snapshot.TryGetValue(key, out var existing))
            {
                Log.Debug
                (
                    "复用 EmbeddingService: 提供商={Provider}, 模型={Model}, 已缓存服务数={ServiceCount}",
                    config.Provider,
                    config.ModelName,
                    snapshot.Count
                );
                return existing;
            }

            var created = new EmbeddingService(config);
            var updated = snapshot.Add(key, created);

            if (ReferenceEquals(Interlocked.CompareExchange(ref services, updated, snapshot), snapshot))
            {
                Log.Information
                (
                    "EmbeddingService 已加入缓存: 提供商={Provider}, 模型={Model}, 已缓存服务数={ServiceCount}",
                    config.Provider,
                    config.ModelName,
                    updated.Count
                );
                return created;
            }
        }
    }

    public void Reset()
    {
        var previous = Interlocked.Exchange(ref services, ImmutableDictionary<string, IEmbeddingService>.Empty);

        if (previous.Count > 0)
            Log.Information("重置 EmbeddingService 缓存: 已清除服务数={ServiceCount}", previous.Count);
    }
}
