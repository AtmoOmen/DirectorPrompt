using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public sealed record EnumTransitionConfig
{
    public string Option { get; init; } = string.Empty;

    public string? ChangeRules { get; init; }

    public EnumTransitionMethod Method { get; init; } = EnumTransitionMethod.Random;

    public float Weight { get; init; } = 1f;

    public string? AttributeName { get; init; }

    public string? Expression { get; init; }

    public EnumSwitchMode SwitchMode { get; init; } = EnumSwitchMode.Always;
}
