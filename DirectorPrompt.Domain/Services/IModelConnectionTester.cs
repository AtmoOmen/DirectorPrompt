namespace DirectorPrompt.Domain.Services;

public interface IModelConnectionTester
{
    Task TestChatAsync(string provider, string endpoint, string? apiKey, string modelName, CancellationToken cancellationToken = default);

    Task TestEmbeddingAsync(string provider, string endpoint, string? apiKey, string modelName, CancellationToken cancellationToken = default);
}
