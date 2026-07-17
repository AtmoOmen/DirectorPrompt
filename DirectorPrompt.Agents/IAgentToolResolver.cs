using DirectorPrompt.Domain.Enums;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents;

public interface IAgentToolResolver
{
    Task<IReadOnlyList<AIFunction>> ResolveAsync
    (
        AgentTaskType      taskType,
        ToolExecutionContext context,
        CancellationToken  cancellationToken = default
    );
}
