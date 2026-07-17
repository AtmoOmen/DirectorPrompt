using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.Prompts;

public static class BuiltInPrompts
{
    public static string Get(AgentTaskType taskType) => taskType switch
    {
        AgentTaskType.Narrator     => NarratorPrompt.SYSTEM,
        AgentTaskType.MemoryUpdate => MemorySubAgentPrompt.UPDATE,
        AgentTaskType.Scene        => SceneAgentPrompt.SYSTEM,
        _                          => throw new ArgumentOutOfRangeException(nameof(taskType))
    };
}
