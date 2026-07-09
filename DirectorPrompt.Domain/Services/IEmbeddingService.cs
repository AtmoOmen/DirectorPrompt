using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.Domain.Services;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

public interface IEmbeddingServiceFactory
{
    IEmbeddingService Create(ResolvedEmbeddingConfig config);
}
