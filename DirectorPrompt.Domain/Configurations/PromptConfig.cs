namespace DirectorPrompt.Domain.Configurations;

public sealed class PromptConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}
