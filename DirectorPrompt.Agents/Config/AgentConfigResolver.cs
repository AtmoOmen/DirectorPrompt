using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using Serilog;

namespace DirectorPrompt.Agents.Config;

public sealed class AgentConfigResolver
(
    OrchestratorConfig config
)
{
    public ResolvedAgentTask? Resolve(AgentTaskType taskType)
    {
        var task = config.AgentTasks.FirstOrDefault(t => t.TaskType == taskType && t.Enabled);

        if (task is null)
        {
            Log.Debug("Agent 任务未启用或不存在: 类型={TaskType}", taskType);
            return null;
        }

        var model = config.Models.FirstOrDefault(m => m.ID == task.ModelConfigID);

        if (model is null)
        {
            Log.Warning("Agent 任务引用的模型不存在: 类型={TaskType}, 模型配置={ModelConfigID}", taskType, task.ModelConfigID);
            return null;
        }

        var provider = config.Providers.FirstOrDefault(p => p.ID == model.ProviderID);

        if (provider is null)
        {
            Log.Warning
            (
                "Agent 模型引用的提供商不存在: 类型={TaskType}, 模型={ModelName}, 提供商配置={ProviderID}",
                taskType,
                model.ModelName,
                model.ProviderID
            );
            return null;
        }

        var systemPrompt = !string.IsNullOrEmpty(task.PromptID) ?
                               config.Prompts.FirstOrDefault(p => p.ID == task.PromptID)?.Content ?? BuiltInPrompts.Get(taskType) :
                               BuiltInPrompts.Get(taskType);

        var modelPrompt = !string.IsNullOrEmpty(model.PromptID) ?
                              config.Prompts.FirstOrDefault(p => p.ID == model.PromptID)?.Content :
                              null;

        var resolved = new ResolvedAgentTask
        (
            taskType,
            model,
            provider,
            systemPrompt,
            modelPrompt
        );

        Log.Debug
        (
            "Agent 任务配置已解析: 类型={TaskType}, 提供商={Provider}, 模型={ModelName}, MCP服务数={MCPServerCount}",
            taskType,
            provider.Provider,
            model.ModelName,
            task.MCPServerIDs.Count
        );

        return resolved;
    }

    public ResolvedEmbeddingConfig? ResolveEmbedding(EmbeddingConfig embeddingConfig)
    {
        var provider = config.Providers.FirstOrDefault(p => p.ID == embeddingConfig.ProviderID);

        if (provider is null)
        {
            Log.Warning("向量模型引用的提供商不存在: 提供商配置={ProviderID}", embeddingConfig.ProviderID);
            return null;
        }

        var resolved = new ResolvedEmbeddingConfig
        {
            Provider      = provider.Provider,
            Endpoint      = provider.Endpoint,
            APIKey        = provider.APIKey,
            ModelName     = embeddingConfig.ModelName,
            CustomHeaders = provider.CustomHeaders
        };

        Log.Debug
        (
            "向量模型配置已解析: 提供商={Provider}, 模型={ModelName}",
            resolved.Provider,
            resolved.ModelName
        );

        return resolved;
    }
}
