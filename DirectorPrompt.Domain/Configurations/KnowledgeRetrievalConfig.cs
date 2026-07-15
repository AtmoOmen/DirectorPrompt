namespace DirectorPrompt.Domain.Configurations;

public sealed class KnowledgeRetrievalConfig
{
    public int SemanticTopK { get; set; } = 8;

    public int TokenBudget { get; set; } = 2000;

    public float MinRelevance { get; set; }
}
