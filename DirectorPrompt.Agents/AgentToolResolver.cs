using DirectorPrompt.Agents.MCP;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class AgentToolResolver
(
    SceneTools               sceneTools,
    KnowledgeTools           knowledgeTools,
    MemoryTools              memoryTools,
    StateTools               stateTools,
    CharacterTools           characterTools,
    UserSettings             userSettings,
    IExternalMCPToolRegistry externalMCPToolRegistry
) : IAgentToolResolver
{
    public async Task<IReadOnlyList<AIFunction>> ResolveAsync
    (
        AgentTaskType        taskType,
        ToolExecutionContext context,
        CancellationToken    cancellationToken = default
    )
    {
        Log.Debug
        (
            "开始解析 Agent 工具: 任务={TaskType}, 项目={ProjectID}, 对话={SessionID}, 轮次={RoundID}",
            taskType,
            context.ProjectID,
            context.SessionID,
            context.RoundID
        );

        var tools = taskType switch
        {
            AgentTaskType.Narrator     => knowledgeTools.Create(context).ToList(),
            AgentTaskType.Scene        => sceneTools.Create(context).ToList(),
            AgentTaskType.MemoryUpdate => CreateMemoryUpdateTools(context),
            _                          => throw new ArgumentOutOfRangeException(nameof(taskType))
        };
        var task = userSettings.Orchestrator.AgentTasks.FirstOrDefault(item => item.TaskType == taskType);

        if (task is null)
        {
            Log.Information("Agent 工具解析完成: 任务={TaskType}, 内置工具数={ToolCount}, 未配置 MCP 服务", taskType, tools.Count);
            return tools;
        }

        var externalToolCount = 0;

        foreach (var serverID in task.MCPServerIDs.Distinct())
        {
            var server = userSettings.MCPServers.FirstOrDefault(item => item.ID == serverID);

            if (server is null)
                continue;

            var externalTools = await externalMCPToolRegistry.GetToolsAsync(server, cancellationToken);
            tools.AddRange(externalTools);
            externalToolCount += externalTools.Count;
        }

        Log.Information
        (
            "Agent 工具解析完成: 任务={TaskType}, 工具总数={ToolCount}, 外部工具数={ExternalToolCount}, 配置 MCP 服务数={MCPServerCount}",
            taskType,
            tools.Count,
            externalToolCount,
            task.MCPServerIDs.Distinct().Count()
        );

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
