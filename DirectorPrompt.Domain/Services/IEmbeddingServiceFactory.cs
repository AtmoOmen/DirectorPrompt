using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.Domain.Services;

public interface IEmbeddingServiceFactory
{
    IEmbeddingService Create(ResolvedEmbeddingConfig config);

    void Reset();
}
