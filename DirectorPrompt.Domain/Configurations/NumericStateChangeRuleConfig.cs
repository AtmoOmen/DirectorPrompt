using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed record NumericStateChangeRuleConfig
{
    public string ID { get; init; } = string.Empty;

    public string Remarks { get; init; } = string.Empty;

    public string? AttributeName { get; init; }

    public string Expression { get; init; } = "true == true";

    public string ChangeExpression { get; init; } = "{val} += 0";

    public SystemTrigger Trigger { get; init; } = SystemTrigger.RoundEnd;

    public EnumSwitchMode SwitchMode { get; init; } = EnumSwitchMode.Always;
}
