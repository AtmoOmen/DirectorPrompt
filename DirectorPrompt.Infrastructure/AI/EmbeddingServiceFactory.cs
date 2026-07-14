using System.Collections.Immutable;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;

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
                return existing;

            var created = new EmbeddingService(config);
            var updated = snapshot.Add(key, created);

            if (ReferenceEquals(Interlocked.CompareExchange(ref services, updated, snapshot), snapshot))
                return created;
        }
    }

    public void Reset() =>
        Interlocked.Exchange(ref services, ImmutableDictionary<string, IEmbeddingService>.Empty);
}
