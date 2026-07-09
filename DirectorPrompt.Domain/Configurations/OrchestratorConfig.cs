namespace DirectorPrompt.Domain.Configurations;

public record OrchestratorConfig
{
    public List<ProviderConfig> Providers { get; set; } = [];

    public List<ModelConfig> Models { get; set; } = [];

    public List<PromptConfig> Prompts { get; set; } = [];

    public List<AgentTaskConfig> AgentTasks { get; set; } = [];

    public AuditConfig AuditConfig { get; init; } = new();

    public MemoryConfig MemoryConfig { get; init; } = new();

    public KnowledgeRetrievalConfig KnowledgeConfig { get; init; } = new();
}
