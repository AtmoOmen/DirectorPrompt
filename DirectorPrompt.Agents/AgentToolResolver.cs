using DirectorPrompt.Agents.MCP;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents;

public sealed class AgentToolResolver
(
    SceneTools                sceneTools,
    KnowledgeTools            knowledgeTools,
    MemoryTools               memoryTools,
    StateTools                stateTools,
    CharacterTools            characterTools,
    UserSettings              userSettings,
    IExternalMCPToolRegistry  externalMCPToolRegistry
) : IAgentToolResolver
{
    public async Task<IReadOnlyList<AIFunction>> ResolveAsync
    (
        AgentTaskType       taskType,
        ToolExecutionContext context,
        CancellationToken   cancellationToken = default
    )
    {
        var tools = taskType switch
        {
            AgentTaskType.Narrator => knowledgeTools.Create(context).ToList(),
            AgentTaskType.Scene => sceneTools.Create(context).ToList(),
            AgentTaskType.MemoryUpdate => CreateMemoryUpdateTools(context),
            _ => throw new ArgumentOutOfRangeException(nameof(taskType))
        };
        var task = userSettings.Orchestrator.AgentTasks.FirstOrDefault(item => item.TaskType == taskType);

        if (task is null)
            return tools;

        foreach (var serverID in task.MCPServerIDs.Distinct())
        {
            var server = userSettings.MCPServers.FirstOrDefault(item => item.ID == serverID);

            if (server is null)
                continue;

            tools.AddRange(await externalMCPToolRegistry.GetToolsAsync(server, cancellationToken));
        }

        return tools;
    }

    private List<AIFunction> CreateMemoryUpdateTools(ToolExecutionContext context)
    {
        List<AIFunction> tools = [];
        tools.AddRange(memoryTools.Create(context));
        tools.AddRange(stateTools.Create(context));
        tools.AddRange(characterTools.Create(context));
        return tools;
    }
}
