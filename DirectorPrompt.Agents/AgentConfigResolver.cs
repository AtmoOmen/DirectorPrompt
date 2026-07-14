using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public sealed class AgentConfigResolver
(
    OrchestratorConfig config
)
{
    public ResolvedAgentTask? Resolve(AgentTaskType taskType)
    {
        var task = config.AgentTasks.FirstOrDefault(t => t.TaskType == taskType && t.Enabled);

        if (task is null)
            return null;

        var model = config.Models.FirstOrDefault(m => m.ID == task.ModelConfigID);

        if (model is null)
            return null;

        var provider = config.Providers.FirstOrDefault(p => p.ID == model.ProviderID);

        if (provider is null)
            return null;

        var systemPrompt = !string.IsNullOrEmpty(task.PromptID) ?
                               config.Prompts.FirstOrDefault(p => p.ID == task.PromptID)?.Content ?? BuiltInPrompts.Get(taskType) :
                               BuiltInPrompts.Get(taskType);

        var modelPrompt = !string.IsNullOrEmpty(model.PromptID) ?
                              config.Prompts.FirstOrDefault(p => p.ID == model.PromptID)?.Content :
                              null;

        return new ResolvedAgentTask
        (
            taskType,
            model,
            provider,
            systemPrompt,
            modelPrompt
        );
    }

    public ResolvedEmbeddingConfig? ResolveEmbedding(EmbeddingConfig embeddingConfig)
    {
        var provider = config.Providers.FirstOrDefault(p => p.ID == embeddingConfig.ProviderID);

        if (provider is null)
            return null;

        return new ResolvedEmbeddingConfig
        {
            Provider      = provider.Provider,
            Endpoint      = provider.Endpoint,
            APIKey        = provider.APIKey,
            ModelName     = embeddingConfig.ModelName,
            CustomHeaders = provider.CustomHeaders
        };
    }
}
