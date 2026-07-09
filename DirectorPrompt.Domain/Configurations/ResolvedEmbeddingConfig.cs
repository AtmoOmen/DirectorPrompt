namespace DirectorPrompt.Domain.Configurations;

public record ResolvedEmbeddingConfig
{
    public string Provider { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public string? APIKey { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string Fingerprint => $"{Provider}|{Endpoint}|{ModelName}";
}
