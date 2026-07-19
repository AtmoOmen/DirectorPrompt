using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Configurations;

public sealed record StateAttributeConfig
{
    public float? Min { get; init; }

    public float? Max { get; init; }

    public float? Initial { get; init; }

    public string? Unit { get; init; }

    public string? ChangeRules { get; init; }

    public List<string>? Options { get; init; }

    public string? Trigger { get; init; }

    public List<EnumTransitionConfig>? Transitions { get; init; }

    public List<Phase> Phases { get; init; } = [];
}
