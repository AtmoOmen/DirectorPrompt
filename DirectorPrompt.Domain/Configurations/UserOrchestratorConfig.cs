namespace DirectorPrompt.Domain.Configurations;

public class UserOrchestratorConfig
{
    public List<ProviderConfig> Providers { get; set; } = [];

    public List<ModelConfig> Models { get; set; } = [];

    public List<PromptConfig> Prompts { get; set; } = [];

    public List<AgentTaskConfig> AgentTasks { get; set; } = [];

    public MemoryConfig MemoryConfig { get; set; } = new();

    public KnowledgeRetrievalConfig KnowledgeConfig { get; set; } = new();
}
