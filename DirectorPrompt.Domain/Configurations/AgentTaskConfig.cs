using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed class AgentTaskConfig
{
    public AgentTaskType TaskType { get; set; }

    public string ModelConfigID { get; set; } = string.Empty;

    public string? PromptID { get; set; }

    public bool Enabled { get; set; } = true;

    public List<string> MCPServerIDs { get; set; } = [];
}
