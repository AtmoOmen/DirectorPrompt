using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Agents;

public sealed record PhaseEvaluationResult : TransitionResult
{
    public required IReadOnlyList<long> ActivatedEntryIDs { get; init; }
}
