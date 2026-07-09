using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public record AgentTaskConfig
{
    public AgentTaskType TaskType { get; init; }

    public string ModelConfigID { get; init; } = string.Empty;

    public string? PromptID { get; init; }

    public bool Enabled { get; init; } = true;
}
