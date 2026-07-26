using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed record EnumTransitionConfig
{
    public string ID { get; init; } = string.Empty;

    public string Option { get; init; } = string.Empty;

    public string Remarks { get; init; } = string.Empty;

    public string? ChangeRules { get; init; }

    public EnumTransitionMethod? Method { get; init; }

    public float Weight { get; init; } = 1f;

    public string? AttributeName { get; init; }

    public string? Expression { get; init; }

    public EnumSwitchMode? SwitchMode { get; init; }

    public SystemTrigger? Trigger { get; init; }

    public List<StateRuleConditionConfig> Conditions { get; init; } = [];

    public StateRuleConditionMatch ConditionMatch { get; init; } = StateRuleConditionMatch.All;

    public StateRuleRepeatPolicy? RepeatPolicy { get; init; }

    public int Priority { get; init; }
}
