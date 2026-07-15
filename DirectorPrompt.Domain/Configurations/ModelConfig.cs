namespace DirectorPrompt.Domain.Configurations;

public sealed class ModelConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string ProviderID { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public float Temperature { get; set; } = 0.8f;

    public string? ReasoningEffort { get; set; }

    public string? ExtraParameters { get; set; }

    public string? PromptID { get; set; }
}
