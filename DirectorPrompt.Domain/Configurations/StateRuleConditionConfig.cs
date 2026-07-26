using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed record StateRuleConditionConfig
{
    public StateRuleConditionSource Source { get; init; } = StateRuleConditionSource.CurrentValue;

    public string? StateName { get; init; }

    public StateRuleComparison Comparison { get; init; } = StateRuleComparison.Equals;

    public string ExpectedValue { get; init; } = string.Empty;
}
