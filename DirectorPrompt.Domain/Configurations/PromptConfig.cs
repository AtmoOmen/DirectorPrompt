namespace DirectorPrompt.Domain.Configurations;

public record PromptConfig
{
    public string ID { get; init; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;
}
