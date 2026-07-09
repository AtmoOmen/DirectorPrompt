using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public record ModelConfig
{
    public string ID { get; init; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; init; } = string.Empty;

    public string ProviderID { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public float Temperature { get; init; } = 0.8f;

    public ReasoningEffort ReasoningEffort { get; init; } = ReasoningEffort.None;

    public string? ExtraParameters { get; init; }

    public string? PromptID { get; init; }
}
