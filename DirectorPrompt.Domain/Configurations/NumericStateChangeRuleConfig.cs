using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed record NumericStateChangeRuleConfig
{
    public string ID { get; init; } = string.Empty;

    public string Remarks { get; init; } = string.Empty;

    public string? AttributeName { get; init; }

    public string? Expression { get; init; }

    public string? ChangeExpression { get; init; }

    public SystemTrigger Trigger { get; init; } = SystemTrigger.RoundEnd;

    public EnumSwitchMode? SwitchMode { get; init; }

    public List<StateRuleConditionConfig> Conditions { get; init; } = [];

    public StateRuleConditionMatch ConditionMatch { get; init; } = StateRuleConditionMatch.All;

    public NumericStateOperation? Operation { get; init; }

    public string? ValueExpression { get; init; }

    public StateRuleRepeatPolicy? RepeatPolicy { get; init; }

    public int Priority { get; init; }
}
