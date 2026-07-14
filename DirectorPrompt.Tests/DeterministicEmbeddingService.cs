using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Tests;

public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    public int GeneratedTextCount { get; private set; }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        GeneratedTextCount++;
        return Task.FromResult(CreateVector(text));
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync
    (
        IReadOnlyList<string> texts,
        CancellationToken     cancellationToken = default
    )
    {
        GeneratedTextCount += texts.Count;
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(CreateVector).ToList());
    }

    private static float[] CreateVector(string text)
    {
        var value = text.Aggregate(17, (current, character) => unchecked((current * 31) + character));
        return [1f, (value & 255) / 255f, ((value >> 8) & 255) / 255f];
    }
}
