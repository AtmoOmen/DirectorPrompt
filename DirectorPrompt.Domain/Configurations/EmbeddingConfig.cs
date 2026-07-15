namespace DirectorPrompt.Domain.Configurations;

public sealed class EmbeddingConfig
{
    public string ProviderID { get; set; } = string.Empty;

    public string ModelName { get; set; } = "text-embedding-v4";
}
