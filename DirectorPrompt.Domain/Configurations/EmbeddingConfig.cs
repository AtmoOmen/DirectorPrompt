namespace DirectorPrompt.Domain.Configurations;

public record EmbeddingConfig
{
    public string ProviderID { get; init; } = string.Empty;

    public string ModelName { get; init; } = "text-embedding-v4";
}
