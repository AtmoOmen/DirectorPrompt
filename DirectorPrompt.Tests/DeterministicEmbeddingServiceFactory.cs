using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Tests;

public sealed class DeterministicEmbeddingServiceFactory
(
    DeterministicEmbeddingService service
) : IEmbeddingServiceFactory
{
    public IEmbeddingService Create(ResolvedEmbeddingConfig config) =>
        service;

    public void Reset()
    {
    }
}
