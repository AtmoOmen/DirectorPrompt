namespace DirectorPrompt.Domain.Configurations;

public sealed record HistoryContextConfig
{
    public int MaxRounds { get; init; } = 40;

    public int TokenBudget { get; init; } = 12000;
}
