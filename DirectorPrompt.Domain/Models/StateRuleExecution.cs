namespace DirectorPrompt.Domain.Models;

public sealed record StateRuleExecution
{
    public long ID { get; init; }

    public long SessionID { get; init; }

    public long RoundID { get; init; }

    public long SceneID { get; init; }

    public long AttributeID { get; init; }

    public long CharacterID { get; init; }

    public string RuleID { get; init; } = string.Empty;

    public string EventKey { get; init; } = string.Empty;

    public bool ConditionMet { get; init; }

    public bool Fired { get; init; }
}
