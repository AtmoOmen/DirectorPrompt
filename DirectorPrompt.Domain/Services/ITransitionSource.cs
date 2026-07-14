using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Services;

public interface ITransitionSource
{
    string SourceName { get; }

    EventType EventType { get; }

    Task<TransitionResult> EvaluateAsync
    (
        long                   projectID,
        long                   sessionID,
        IReadOnlyList<string>? previousKeys,
        CancellationToken      cancellationToken = default
    );
}
