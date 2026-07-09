namespace DirectorPrompt.Domain.Services;

public interface IModelConnectionTester
{
    Task<IReadOnlyList<string>> FetchModelsAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        CancellationToken cancellationToken = default
    );

    Task TestChatAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        string            modelName,
        CancellationToken cancellationToken = default
    );

    Task TestEmbeddingAsync
    (
        string            provider,
        string            endpoint,
        string?           apiKey,
        string            modelName,
        CancellationToken cancellationToken = default
    );
}
