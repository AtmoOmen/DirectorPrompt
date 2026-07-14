using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public sealed record ResolvedAgentTask
(
    AgentTaskType  TaskType,
    ModelConfig    ModelConfig,
    ProviderConfig ProviderConfig,
    string         SystemPrompt,
    string?        ModelPrompt
);
