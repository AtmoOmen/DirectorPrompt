namespace DirectorPrompt.Domain.Configurations;

public sealed class MemoryConfig
{
    public int RecallTopK { get; set; } = 10;

    public int TokenBudget { get; set; } = 1500;

    public float MinRelevance { get; set; }

    public float TimeDecayLambda { get; set; }
}
